using System.Security.Cryptography;
using System.Text;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed class CommerceService(SecurityDbContext db, SqlSecurityTransaction transactions,
    ProductInformationService products, TimeProvider clock)
{
    private async Task<AppUser> Client(Actor actor, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == actor.UserId && u.IsActive && u.RoleName == Roles.Client, ct)
        ?? throw new SecurityFault("forbidden", "Esta función requiere una cuenta de cliente.", 403);
    private async Task<AppUser> Staff(Actor actor, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == actor.UserId && u.IsActive &&
            (u.RoleName == Roles.Administrator || u.RoleName == Roles.Logistics), ct)
        ?? throw new SecurityFault("forbidden", "Acceso no permitido.", 403);
    private void Audit(Actor actor, AppUser user, string action, Guid id, RequestOrigin origin) => db.Events.Add(new SecurityEvent
    {
        OccurredAtUtc = clock.GetUtcNow(), ActorUserId = user.Id, ActorName = user.Name, ActorEmail = user.Email,
        ActorRole = user.RoleName, SessionId = actor.SessionId, Action = action, Outcome = "Exitoso", Module = "Compras y pedidos",
        Entity = action.StartsWith("cart", StringComparison.Ordinal) ? "cart_lines" : "customer_orders", EntityId = id.ToString(),
        Origin = origin.Ip, CorrelationId = origin.CorrelationId
    });
    private async Task ValidateSelection(Actor actor, StoreProductView product, string size, Guid? petId, CancellationToken ct)
    {
        if (product.StockQuantity <= 0) throw new SecurityFault("out_of_stock", "Agotado. Elige otro producto.", 409);
        if (!product.Sizes.Any(s => s.Name == size)) throw new SecurityFault("invalid_size", "Selecciona una talla publicada para este producto.");
        if (petId is null) return;
        var pet = await db.Pets.SingleOrDefaultAsync(p => p.Id == petId && p.OwnerUserId == actor.UserId, ct)
            ?? throw new SecurityFault("pet_not_found", "Mascota no encontrada.", 404);
        if (!pet.IsActive || !pet.AllergiesDeclared || pet.WeightKg <= 0 || pet.HeightCm <= 0)
            throw new SecurityFault("incomplete_pet", "Actualiza las medidas, las alergias y el estado activo del perfil antes de continuar.");
        if (string.IsNullOrWhiteSpace(product.Material) || ProductSizing.AllergyConflict(product.Material, pet.Allergies))
            throw new SecurityFault("allergic_material", "Material alergico para tu mascota o material sin declarar.");
        var advice = ProductSizing.Evaluate(product, pet.Species, pet.HeightCm, pet.WeightKg);
        if (advice.SuggestedSize != size) throw new SecurityFault("invalid_size", "La talla ya no coincide de forma segura con el perfil. Solicita nuevas recomendaciones.", 409);
    }
    public async Task<CartAddResult> AddAsync(Actor actor, AddCartRequest request, RequestOrigin origin, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Quantity is < 1 or > 20) throw new SecurityFault("invalid_cart", "Solicitud de carrito inválida.");
        var size = request.Size?.Trim() ?? "";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.ProductId}|{request.PetId}|{size}|{request.Quantity}")));
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var prior = await db.CartOperations.SingleOrDefaultAsync(o => o.OwnerUserId == actor.UserId && o.RequestId == request.RequestId, ct);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint) throw new SecurityFault("reused_request", "No reutilices una solicitud para otra selección.", 409);
            await tx.CommitAsync(ct); return new(request.RequestId, "Producto agregado al carrito.", prior.ResultQuantity);
        }
        var product = await products.ProductAsync(request.ProductId, ct);
        await ValidateSelection(actor, product, size, request.PetId, ct);
        var used = await db.CartLines.Where(l => l.OwnerUserId == actor.UserId && l.ProductId == request.ProductId).SumAsync(l => l.Quantity, ct);
        if (used + request.Quantity > product.StockQuantity) throw new SecurityFault("insufficient_stock", "La cantidad supera el stock disponible.", 409);
        var line = await db.CartLines.SingleOrDefaultAsync(l => l.OwnerUserId == actor.UserId && l.ProductId == request.ProductId &&
            l.PetId == request.PetId && l.Size == size, ct);
        if (line is null) { line = new CartLine { OwnerUserId = actor.UserId, ProductId = request.ProductId, PetId = request.PetId, Size = size }; db.CartLines.Add(line); }
        line.Quantity += request.Quantity;
        db.CartOperations.Add(new CartOperation { OwnerUserId = actor.UserId, RequestId = request.RequestId,
            Fingerprint = fingerprint, ResultQuantity = line.Quantity, CreatedAtUtc = clock.GetUtcNow() });
        Audit(actor, user, "cart_add", line.Id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(request.RequestId, "Producto agregado al carrito.", line.Quantity);
    }
    public async Task<CartView> CartAsync(Actor actor, CancellationToken ct)
    {
        await Client(actor, ct);
        var lines = await (from l in db.CartLines.AsNoTracking()
            join p in db.BaseProducts.AsNoTracking() on l.ProductId equals p.Id
            where l.OwnerUserId == actor.UserId
            select new CartLineView(l.Id, p.Id, p.Name, p.Sku, l.Size, l.PetId,
                db.Pets.Where(pet => pet.Id == l.PetId).Select(pet => pet.Name).FirstOrDefault(), l.Quantity, p.UnitPrice,
                p.IsActive && db.Categories.Any(c => c.Id == p.CategoryId && c.IsActive) ? p.StockQuantity : 0)).ToListAsync(ct);
        return new(lines, lines.Sum(l => l.Quantity * l.UnitPrice));
    }
    public async Task RemoveAsync(Actor actor, Guid id, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var line = await db.CartLines.SingleOrDefaultAsync(l => l.Id == id && l.OwnerUserId == actor.UserId, ct)
            ?? throw new SecurityFault("cart_not_found", "Elemento no encontrado.", 404);
        db.CartLines.Remove(line); Audit(actor, user, "cart_remove", id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<OrderView> CheckoutAsync(Actor actor, CheckoutRequest request, RequestOrigin origin, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty) throw new SecurityFault("invalid_request", "Solicitud inválida.");
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var existing = await db.Orders.SingleOrDefaultAsync(o => o.OwnerUserId == actor.UserId && o.CheckoutRequestId == request.RequestId, ct);
        if (existing is not null) { await tx.CommitAsync(ct); return await View(existing, ct); }
        var address = await db.DeliveryAddresses.SingleOrDefaultAsync(a => a.Id == request.AddressId && a.OwnerUserId == actor.UserId, ct)
            ?? throw new SecurityFault("invalid_address", "Selecciona una dirección propia.");
        var lines = await db.CartLines.Where(l => l.OwnerUserId == actor.UserId).ToListAsync(ct);
        if (lines.Count == 0) throw new SecurityFault("empty_cart", "Tu carrito está vacío.");
        var items = new List<CustomerOrderItem>();
        var order = new CustomerOrder { OwnerUserId = actor.UserId, CheckoutRequestId = request.RequestId,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow(),
            DeliverySnapshot = $"{address.Province}, {address.Canton}, {address.District}. {address.ExactAddress}. Referencia: {address.Reference}" };
        order.Number = "AML-" + order.Id.ToString("N").ToUpperInvariant();
        foreach (var group in lines.GroupBy(l => l.ProductId))
        {
            var product = await products.ProductAsync(group.Key, ct);
            if (group.Sum(l => l.Quantity) > product.StockQuantity) throw new SecurityFault("insufficient_stock", $"El stock de {product.Name} cambió. Revisa tu carrito.", 409);
            foreach (var line in group)
            {
                await ValidateSelection(actor, product, line.Size, line.PetId, ct);
                var pet = line.PetId.HasValue ? await db.Pets.SingleAsync(p => p.Id == line.PetId && p.OwnerUserId == actor.UserId, ct) : null;
                items.Add(new CustomerOrderItem { OrderId = order.Id, ProductId = product.Id, PetId = line.PetId, ProductName = product.Name,
                    Sku = product.Sku, Size = line.Size, Quantity = line.Quantity, UnitPrice = product.UnitPrice,
                    PetSnapshot = pet is null ? "Sin mascota vinculada" : $"{pet.Name}; {pet.Species}; {pet.Breed}; {pet.AgeYears} años; {pet.WeightKg} kg; {pet.HeightCm} cm; Alergias: {(pet.Allergies.Length > 0 ? pet.Allergies : "Sin alergias conocidas")}" });
            }
            var tracked = await db.BaseProducts.SingleAsync(p => p.Id == group.Key, ct);
            tracked.StockQuantity -= group.Sum(l => l.Quantity); tracked.UpdatedAtUtc = clock.GetUtcNow();
        }
        order.Total = items.Sum(i => i.Quantity * i.UnitPrice);
        db.Orders.Add(order); db.OrderItems.AddRange(items); db.CartLines.RemoveRange(lines);
        Audit(actor, user, "order_create", order.Id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }
    private async Task<OrderView> View(CustomerOrder order, CancellationToken ct) => new(order.Id, order.Number,
        order.Status, order.UpdatedAtUtc, order.Carrier, order.TrackingNumber, order.CreatedAtUtc, order.Total,
        order.DeliverySnapshot, await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id)
            .Select(i => new OrderItemView(i.ProductName, i.Sku, i.Size, i.Quantity, i.UnitPrice, i.PetSnapshot)).ToListAsync(ct));
    public async Task<OrderView?> OwnOrderAsync(Actor actor, string number, CancellationToken ct)
    {
        await Client(actor, ct);
        var normalized = number.Trim().ToUpperInvariant();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.OwnerUserId == actor.UserId && o.Number == normalized, ct);
        return order is null ? null : await View(order, ct);
    }
    public async Task<List<OrderView>> OrdersAsync(Actor actor, bool staff, CancellationToken ct)
    {
        if (staff) await Staff(actor, ct); else await Client(actor, ct);
        var rows = db.Orders.AsNoTracking().AsQueryable();
        if (!staff) rows = rows.Where(o => o.OwnerUserId == actor.UserId);
        var orders = await rows.OrderByDescending(o => o.CreatedAtUtc).Take(100).ToListAsync(ct);
        var result = new List<OrderView>(); foreach (var order in orders) result.Add(await View(order, ct)); return result;
    }
    public async Task<OrderView> UpdateOrderAsync(Actor actor, Guid id, OrderUpdateRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", "ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new SecurityFault("order_not_found", "Pedido no encontrado.", 404);
        var allowed = request.Status == order.Status || (order.Status switch
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
        if (request.Status == OrderStatuses.Cancelled && order.Status != OrderStatuses.Cancelled)
        {
            var items = await db.OrderItems.Where(i => i.OrderId == id).ToListAsync(ct);
            foreach (var group in items.GroupBy(i => i.ProductId))
            { var product = await db.BaseProducts.SingleAsync(p => p.Id == group.Key, ct); product.StockQuantity += group.Sum(i => i.Quantity); product.UpdatedAtUtc = clock.GetUtcNow(); }
        }
        order.Status = request.Status; order.Carrier = request.Carrier?.Trim() ?? ""; order.TrackingNumber = request.TrackingNumber?.Trim() ?? "";
        order.UpdatedAtUtc = clock.GetUtcNow(); Audit(actor, user, "order_status_update", id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }
}
