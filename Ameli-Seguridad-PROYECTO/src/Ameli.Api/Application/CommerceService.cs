using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ameli.Api.Application;

public sealed class CommerceService(SecurityDbContext db, SqlSecurityTransaction transactions,
    ProductInformationService products, TimeProvider clock, IPaymentGateway payments, IOptions<CommerceOptions> options)
{
    private async Task<AppUser> Client(Actor actor, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(user => user.Id == actor.UserId && user.IsActive && user.RoleName == Roles.Client, ct)
        ?? throw new SecurityFault("forbidden", "Esta función requiere una cuenta de cliente.", 403);

    private async Task<AppUser> Staff(Actor actor, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(user => user.Id == actor.UserId && user.IsActive &&
            (user.RoleName == Roles.Administrator || user.RoleName == Roles.Logistics), ct)
        ?? throw new SecurityFault("forbidden", "Acceso no permitido.", 403);

    private void Audit(Actor actor, AppUser user, string action, Guid id, RequestOrigin origin, string outcome = "Exitoso") =>
        db.Events.Add(new SecurityEvent
        {
            OccurredAtUtc = clock.GetUtcNow(), ActorUserId = user.Id, ActorName = user.Name, ActorEmail = user.Email,
            ActorRole = user.RoleName, SessionId = actor.SessionId, Action = action, Outcome = outcome,
            Module = "Compras y pedidos", Entity = action.StartsWith("cart", StringComparison.Ordinal) ? "cart_lines" : action.StartsWith("payment", StringComparison.Ordinal) ? "payment_attempts" : "customer_orders",
            EntityId = id.ToString(), Origin = origin.Ip, CorrelationId = origin.CorrelationId
        });

    private async Task ValidateSelection(Actor actor, StoreProductView product, string size, Guid? petId,
        bool acknowledgesUnspecifiedSize, CancellationToken ct)
    {
        if (product.StockQuantity <= 0) throw new SecurityFault("out_of_stock", "Producto agotado", 409);
        if (product.Sizes.Count == 0)
        {
            if (size.Length > 0 || petId.HasValue)
                throw new SecurityFault("invalid_size", "Este producto no tiene tallas publicadas. Solo puedes comprarlo sin talla y sin mascota vinculada.");
            if (!acknowledgesUnspecifiedSize)
                throw new SecurityFault("size_confirmation_required", "Confirma que deseas comprar este producto sin talla especificada.");
            return;
        }
        if (!product.Sizes.Any(rule => rule.Name == size))
            throw new SecurityFault("invalid_size", "Selecciona una talla publicada para este producto.");
        if (petId is null) return;
        var pet = await db.Pets.SingleOrDefaultAsync(item => item.Id == petId && item.OwnerUserId == actor.UserId, ct)
            ?? throw new SecurityFault("pet_not_found", "Mascota no encontrada.", 404);
        if (!pet.IsActive || !pet.AllergiesDeclared || pet.WeightKg <= 0 || pet.HeightCm <= 0)
            throw new SecurityFault("incomplete_pet", "Actualiza las medidas, las alergias y el estado activo del perfil antes de continuar.");
        if (string.IsNullOrWhiteSpace(product.Material) || ProductSizing.AllergyConflict(product.Material, pet.Allergies))
            throw new SecurityFault("allergic_material", "Material alergico para tu mascota o material sin declarar.");
        if (ProductSizing.Evaluate(product, pet.Species, pet.HeightCm, pet.WeightKg).SuggestedSize != size)
            throw new SecurityFault("invalid_size", "La talla ya no coincide de forma segura con el perfil. Solicita nuevas recomendaciones.", 409);
    }

    public async Task<CartAddResult> AddAsync(Actor actor, AddCartRequest request, RequestOrigin origin, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Quantity is < 1 or > 100000)
            throw new SecurityFault("invalid_cart", "Solicitud de carrito inválida.");
        var size = request.Size?.Trim() ?? "";
        var selection = $"{request.ProductId}|{request.PetId}|{size}|{request.Quantity}";
        if (request.AcknowledgesUnspecifiedSize) selection += "|unspecified";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selection)));
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var prior = await db.CartOperations.SingleOrDefaultAsync(operation => operation.OwnerUserId == actor.UserId && operation.RequestId == request.RequestId, ct);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint) throw new SecurityFault("reused_request", "No reutilices una solicitud para otra selección.", 409);
            await tx.CommitAsync(ct);
            return new(request.RequestId, "Producto agregado al carrito.", prior.ResultQuantity, await CartAsync(actor, ct));
        }
        var product = await products.ProductAsync(request.ProductId, ct);
        await ValidateSelection(actor, product, size, request.PetId, request.AcknowledgesUnspecifiedSize, ct);
        var used = await db.CartLines.Where(line => line.OwnerUserId == actor.UserId && line.ProductId == request.ProductId).SumAsync(line => line.Quantity, ct);
        if (used + request.Quantity > product.StockQuantity)
            throw new SecurityFault("insufficient_stock", "Existencias de artículo superadas", 409);
        var line = await db.CartLines.SingleOrDefaultAsync(item => item.OwnerUserId == actor.UserId && item.ProductId == request.ProductId &&
            item.PetId == request.PetId && item.Size == size, ct);
        if (line is null)
        {
            line = new CartLine { OwnerUserId = actor.UserId, ProductId = request.ProductId, PetId = request.PetId, Size = size };
            db.CartLines.Add(line);
        }
        line.Quantity += request.Quantity;
        line.Revision = Guid.NewGuid();
        db.CartOperations.Add(new CartOperation
        {
            OwnerUserId = actor.UserId, RequestId = request.RequestId, Fingerprint = fingerprint,
            ResultQuantity = line.Quantity, CreatedAtUtc = clock.GetUtcNow()
        });
        Audit(actor, user, "cart_add", line.Id, origin);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(request.RequestId, "Producto agregado al carrito.", line.Quantity, await CartAsync(actor, ct));
    }

    public async Task<CartView> CartAsync(Actor actor, CancellationToken ct)
    {
        await Client(actor, ct);
        var rows = await (from line in db.CartLines.AsNoTracking()
            join product in db.BaseProducts.AsNoTracking() on line.ProductId equals product.Id
            where line.OwnerUserId == actor.UserId
            select new CartLineView(line.Id, product.Id, product.Name, product.Sku, line.Size, line.PetId,
                db.Pets.Where(pet => pet.Id == line.PetId).Select(pet => pet.Name).FirstOrDefault(), line.Quantity, product.UnitPrice,
                product.IsActive && db.Categories.Any(category => category.Id == product.CategoryId && category.IsActive) ? product.StockQuantity : 0,
                line.Revision, 0, null)).ToListAsync(ct);
        var ids = rows.Select(line => line.ProductId).Distinct().ToArray();
        var sizes = await db.ProductSizes.AsNoTracking().Where(rule => ids.Contains(rule.ProductId)).ToListAsync(ct);
        var totals = rows.GroupBy(line => line.ProductId).ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        var lines = rows.Select(line =>
        {
            var maximum = Math.Max(0, line.AvailableStock - (totals[line.ProductId] - line.Quantity));
            var rules = sizes.Where(rule => rule.ProductId == line.ProductId).ToArray();
            var invalidSize = rules.Length == 0 ? line.Size.Length > 0 : !rules.Any(rule => rule.Name == line.Size);
            var issue = line.Quantity > maximum
                ? "El stock cambió. Confirma una cantidad disponible o elimina el artículo."
                : invalidSize ? "Las tallas del producto cambiaron. Elimina esta selección y vuelve al catálogo." : null;
            return line with { MaximumQuantity = maximum, Issue = issue };
        }).ToList();
        var subtotal = lines.Sum(line => line.Subtotal);
        var discount = decimal.Round(subtotal * options.Value.DiscountPercent / 100m, 2);
        var tax = decimal.Round((subtotal - discount) * options.Value.TaxPercent / 100m, 2);
        var revisionData = JsonSerializer.Serialize(new
        {
            Lines = lines.OrderBy(line => line.Id).Select(line => new { line.Id, line.Revision, line.Quantity, line.UnitPrice, line.AvailableStock, line.Issue }),
            Subtotal = subtotal, Discount = discount, Tax = tax
        });
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionData)));
        return new(lines, subtotal - discount + tax, subtotal, discount, tax,
            lines.Count > 0 && lines.All(line => line.Issue is null), revision);
    }

    public async Task<CartView> UpdateAsync(Actor actor, Guid id, UpdateCartRequest request, RequestOrigin origin, CancellationToken ct)
    {
        if (request.Quantity is < 1 or > 100000 || request.Revision is null)
            throw new SecurityFault("invalid_quantity", "Indica una cantidad entera mayor que cero.")
                { Errors = new() { ["Quantity"] = ["Indica una cantidad entera mayor que cero."] } };
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var line = await db.CartLines.SingleOrDefaultAsync(item => item.Id == id && item.OwnerUserId == actor.UserId, ct)
            ?? throw new SecurityFault("cart_not_found", "Elemento no encontrado.", 404);
        if (line.Revision != request.Revision && line.Quantity != request.Quantity)
            throw new SecurityFault("cart_conflict", "El carrito cambió en otra ventana. Actualízalo antes de guardar.", 409);
        var product = await products.ProductAsync(line.ProductId, ct);
        await ValidateSelection(actor, product, line.Size, line.PetId, true, ct);
        var other = await db.CartLines.Where(item => item.OwnerUserId == actor.UserId && item.ProductId == line.ProductId && item.Id != id)
            .SumAsync(item => item.Quantity, ct);
        if (other + request.Quantity > product.StockQuantity)
            throw new SecurityFault("insufficient_stock", "Existencias de artículo superadas", 409);
        if (line.Quantity != request.Quantity)
        {
            line.Quantity = request.Quantity;
            line.Revision = Guid.NewGuid();
            Audit(actor, user, "cart_quantity_update", id, origin);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await CartAsync(actor, ct);
    }

    public async Task RemoveAsync(Actor actor, Guid id, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var line = await db.CartLines.SingleOrDefaultAsync(item => item.Id == id && item.OwnerUserId == actor.UserId, ct)
            ?? throw new SecurityFault("cart_not_found", "Elemento no encontrado.", 404);
        db.CartLines.Remove(line);
        Audit(actor, user, "cart_remove", id, origin);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<OrderView> CheckoutAsync(Actor actor, CheckoutRequest request, RequestOrigin origin, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty) throw new SecurityFault("invalid_request", "Solicitud inválida.");
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var existing = await db.Orders.SingleOrDefaultAsync(order => order.OwnerUserId == actor.UserId && order.CheckoutRequestId == request.RequestId, ct);
        if (existing is not null) { await tx.CommitAsync(ct); return await View(existing, ct); }
        var priorPayment = await db.PaymentAttempts.SingleOrDefaultAsync(attempt => attempt.OwnerUserId == actor.UserId && attempt.RequestId == request.RequestId, ct);
        if (priorPayment is not null)
            throw new SecurityFault("payment_declined", "Pago rechazado, intenta con otra tarjeta", 402);
        var address = await db.DeliveryAddresses.SingleOrDefaultAsync(item => item.Id == request.AddressId && item.OwnerUserId == actor.UserId, ct);
        if (address is null || new[] { address.Province, address.Canton, address.District, address.ExactAddress }.Any(string.IsNullOrWhiteSpace))
            throw new SecurityFault("invalid_address", "Dirección de envío no especificada");
        var cart = await CartAsync(actor, ct);
        if (cart.Lines.Count == 0) throw new SecurityFault("empty_cart", "Tu carrito está vacío.");
        var selections = new Dictionary<Guid, StoreProductView>();
        foreach (var group in cart.Lines.GroupBy(line => line.ProductId))
        {
            var product = await products.ProductAsync(group.Key, ct);
            if (group.Sum(line => line.Quantity) > product.StockQuantity)
                throw new SecurityFault("insufficient_stock", "El stock cambió. Revisa y confirma las cantidades del carrito.", 409);
            foreach (var line in group)
                await ValidateSelection(actor, product, line.Size, line.PetId, true, ct);
            selections.Add(group.Key, product);
        }
        if (request.CartRevision.Length > 0 && request.CartRevision != cart.Revision)
            throw new SecurityFault("cart_changed", "El carrito cambió. Revisa los importes y confirma nuevamente.", 409);

        var lines = await db.CartLines.Where(line => line.OwnerUserId == actor.UserId).ToListAsync(ct);
        var order = new CustomerOrder
        {
            OwnerUserId = actor.UserId, CheckoutRequestId = request.RequestId,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow(),
            DeliverySnapshot = $"{address.Province}, {address.Canton}, {address.District}. {address.ExactAddress}. Referencia: {address.Reference}",
            Subtotal = cart.Subtotal, Discount = cart.Discount, Tax = cart.Tax, Total = cart.Total
        };
        order.Number = "AML-" + order.Id.ToString("N").ToUpperInvariant();
        var items = new List<CustomerOrderItem>();
        foreach (var line in lines)
        {
            var product = selections[line.ProductId];
            var pet = line.PetId.HasValue ? await db.Pets.SingleAsync(item => item.Id == line.PetId && item.OwnerUserId == actor.UserId, ct) : null;
            items.Add(new CustomerOrderItem
            {
                OrderId = order.Id, ProductId = product.Id, PetId = line.PetId, ProductName = product.Name,
                Sku = product.Sku, Size = line.Size, Quantity = line.Quantity, UnitPrice = product.UnitPrice,
                PetSnapshot = pet is null ? "Sin mascota vinculada" : $"{pet.Name}; {pet.Species}; {pet.Breed}; {pet.AgeYears} años; {pet.WeightKg} kg; {pet.HeightCm} cm; Alergias: {(pet.Allergies.Length > 0 ? pet.Allergies : "Sin alergias conocidas")}" 
            });
        }
        var authorization = await payments.AuthorizeAsync(actor.UserId, request, cart.Total, ct);
        var payment = new PaymentAttempt
        {
            OwnerUserId = actor.UserId, RequestId = request.RequestId, Amount = cart.Total,
            Provider = authorization.Provider, Reference = authorization.Reference,
            Status = authorization.Approved ? "Aprobado" : "Rechazado", OccurredAtUtc = clock.GetUtcNow()
        };
        db.PaymentAttempts.Add(payment);
        if (!authorization.Approved)
        {
            Audit(actor, user, "payment_declined", payment.Id, origin, "Fallido");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new SecurityFault("payment_declined", "Pago rechazado, intenta con otra tarjeta", 402);
        }
        foreach (var group in lines.GroupBy(line => line.ProductId))
        {
            var product = await db.BaseProducts.SingleAsync(item => item.Id == group.Key, ct);
            product.StockQuantity -= group.Sum(line => line.Quantity);
            product.UpdatedAtUtc = clock.GetUtcNow();
            product.Revision = Guid.NewGuid();
        }
        order.PaymentMode = authorization.Provider;
        order.PaymentReference = authorization.Reference;
        db.Orders.Add(order);
        db.OrderItems.AddRange(items);
        db.CartLines.RemoveRange(lines);
        db.OrderHistory.Add(new OrderStatusHistory { OrderId = order.Id, Status = OrderStatuses.Confirmed, OccurredAtUtc = order.CreatedAtUtc });
        Audit(actor, user, "order_create", order.Id, origin);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await View(order, ct);
    }

    private static string NormalizeStatus(string status) => status == OrderStatuses.LegacyShipped ? OrderStatuses.Shipped : status;

    private async Task<OrderView> View(CustomerOrder order, CancellationToken ct)
    {
        var history = await db.OrderHistory.AsNoTracking().Where(item => item.OrderId == order.Id)
            .OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Sequence).Select(item => new OrderStatusView(item.Status, item.OccurredAtUtc, item.Carrier, item.TrackingNumber)).ToListAsync(ct);
        if (history.Count == 0) history.Add(new(NormalizeStatus(order.Status), order.UpdatedAtUtc, order.Carrier, order.TrackingNumber));
        return new(order.Id, order.Number, NormalizeStatus(order.Status), order.UpdatedAtUtc, order.Carrier, order.TrackingNumber,
            order.CreatedAtUtc, order.Total, order.DeliverySnapshot,
            await db.OrderItems.AsNoTracking().Where(item => item.OrderId == order.Id)
                .Select(item => new OrderItemView(item.ProductName, item.Sku, item.Size, item.Quantity, item.UnitPrice, item.PetSnapshot)).ToListAsync(ct),
            history, order.Subtotal == 0 && order.Total > 0 ? order.Total : order.Subtotal, order.Discount, order.Tax,
            order.PaymentMode, order.PaymentReference);
    }

    public async Task<OrderView?> OwnOrderAsync(Actor actor, string number, CancellationToken ct)
    {
        await Client(actor, ct);
        var normalized = number.Trim().ToUpperInvariant();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == actor.UserId && item.Number == normalized, ct);
        return order is null ? null : await View(order, ct);
    }

    public async Task<List<OrderView>> OrdersAsync(Actor actor, bool staff, CancellationToken ct)
    {
        if (staff) await Staff(actor, ct); else await Client(actor, ct);
        var rows = db.Orders.AsNoTracking().AsQueryable();
        if (!staff) rows = rows.Where(order => order.OwnerUserId == actor.UserId);
        var orders = await rows.OrderByDescending(order => order.CreatedAtUtc).ToListAsync(ct);
        var result = new List<OrderView>();
        foreach (var order in orders) result.Add(await View(order, ct));
        return result;
    }

    public async Task<OrderView> UpdateOrderAsync(Actor actor, Guid id, OrderUpdateRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", "ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct);
        var order = await db.Orders.SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new SecurityFault("order_not_found", "Pedido no encontrado.", 404);
        request.Status = NormalizeStatus(request.Status);
        var current = NormalizeStatus(order.Status);
        var allowed = request.Status == current || (current switch
        {
            OrderStatuses.Confirmed => request.Status is OrderStatuses.Preparing or OrderStatuses.Shipped or OrderStatuses.Cancelled,
            OrderStatuses.Preparing => request.Status is OrderStatuses.Shipped or OrderStatuses.Cancelled,
            OrderStatuses.Shipped => request.Status == OrderStatuses.Delivered,
            _ => false
        });
        if (!allowed) throw new SecurityFault("invalid_transition", "Ese cambio de estado no está permitido.", 409);
        if (request.Status is OrderStatuses.Shipped or OrderStatuses.Delivered &&
            (string.IsNullOrWhiteSpace(request.Carrier) || string.IsNullOrWhiteSpace(request.TrackingNumber)))
            throw new SecurityFault("missing_tracking", "Indica transportista y guía para un pedido enviado.");
        if (request.Status == OrderStatuses.Cancelled && current != OrderStatuses.Cancelled)
        {
            foreach (var group in (await db.OrderItems.Where(item => item.OrderId == id).ToListAsync(ct)).GroupBy(item => item.ProductId))
            {
                var product = await db.BaseProducts.SingleAsync(item => item.Id == group.Key, ct);
                product.StockQuantity += group.Sum(item => item.Quantity);
                product.UpdatedAtUtc = clock.GetUtcNow();
                product.Revision = Guid.NewGuid();
            }
        }
        order.Status = request.Status;
        order.Carrier = request.Carrier?.Trim() ?? "";
        order.TrackingNumber = request.TrackingNumber?.Trim() ?? "";
        order.UpdatedAtUtc = clock.GetUtcNow();
        if (current != order.Status)
            db.OrderHistory.Add(new OrderStatusHistory
            {
                OrderId = id, Status = order.Status, OccurredAtUtc = order.UpdatedAtUtc,
                Carrier = order.Carrier, TrackingNumber = order.TrackingNumber
            });
        Audit(actor, user, "order_status_update", id, origin);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await View(order, ct);
    }
}
