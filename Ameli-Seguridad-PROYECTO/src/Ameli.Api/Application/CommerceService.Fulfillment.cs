using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed partial class CommerceService
{
    private readonly IDataProtector mailProtector = protection.CreateProtector("Ameli.EmailOutbox.v1");
    private async Task<CustomerOrder> GetOrder(Guid id, CancellationToken ct) =>
        await db.Orders.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new SecurityFault("order_not_found", "Pedido no encontrado.", 404);

    private void QueueOrderEmail(string recipient, string subject, string body) => db.Emails.Add(new OutgoingEmail
    { Recipient = recipient, Subject = subject, ProtectedBody = mailProtector.Protect(body), CreatedAtUtc = clock.GetUtcNow(), NextAttemptAtUtc = clock.GetUtcNow() });

    private void ChangeStatus(CustomerOrder order, string status, AppUser user)
    {
        order.Status = status; order.UpdatedAtUtc = clock.GetUtcNow();
        db.OrderHistory.Add(new OrderStatusHistory { OrderId = order.Id, Status = status, OccurredAtUtc = order.UpdatedAtUtc,
            Carrier = order.Carrier, TrackingNumber = order.TrackingNumber, ActorUserId = user.Id, ActorName = user.Name });
    }

    private static SecurityFault InvalidFields(string code, string message, Dictionary<string, string[]> errors, int status = 400) =>
        new(code, message, status) { Errors = errors };

    private static void Responsible(CustomerOrder order, AppUser user)
    {
        if (order.PreparationUserId is null)
            throw new SecurityFault("preparation_required", "El pedido debe tener un responsable de preparación.", 409);
        if (order.PreparationUserId != user.Id && user.RoleName != Roles.Administrator)
            throw new SecurityFault("assigned_to_another_user", $"Responsable: {order.PreparationUserName}. Inicio: {order.PreparationStartedAtUtc:O}.", 409);
    }

    private async Task RequirePaid(CustomerOrder order, CancellationToken ct)
    {
        if (order.PaymentStatus != "Pagado" || !await db.PaymentAttempts.AnyAsync(x => x.OwnerUserId == order.OwnerUserId &&
            x.RequestId == order.CheckoutRequestId && x.Status == "Aprobado" && x.Amount == order.Total && x.Reference == order.PaymentReference, ct))
            throw new SecurityFault("payment_required", "El pago debe estar autorizado y registrado antes de iniciar la preparación.", 409);
    }

    private static Dictionary<Guid, int> Quantities(OrderLineQuantity[]? input, List<CustomerOrderItem> items)
    {
        var values = input ?? [];
        if (values.Length != items.Count || values.Any(x => x is null) || values.Select(x => x.ItemId).Distinct().Count() != items.Count ||
            values.Any(x => !items.Any(item => item.Id == x.ItemId) || x.Quantity is < 0 or > 100000))
            throw InvalidFields("invalid_items", "Indica una cantidad válida para cada artículo del pedido.", new() { ["Items"] = ["Incluye todos los artículos, una sola vez, con cantidades enteras."] });
        return values.ToDictionary(x => x.ItemId, x => x.Quantity);
    }

    public async Task<OrderView> StartPreparationAsync(Actor actor, Guid id, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", "ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct); var order = await GetOrder(id, ct);
        if (order.Status == OrderStatuses.Preparing && order.PreparationUserId is not null)
        {
            if (order.PreparationUserId != user.Id)
                throw new SecurityFault("already_assigned", $"El pedido está en preparación por {order.PreparationUserName} desde {order.PreparationStartedAtUtc:O}.", 409);
            await tx.CommitAsync(ct); return await View(order, ct);
        }
        if (order.Status != OrderStatuses.Confirmed && !(order.Status == OrderStatuses.Preparing && order.PreparationUserId is null))
            throw new SecurityFault("invalid_transition", "Solo puedes iniciar la preparación de un pedido Confirmado y pagado.", 409);
        await RequirePaid(order, ct);
        var items = await db.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        var differences = items.Where(x => x.Quantity <= 0 || x.ReservedQuantity != x.Quantity)
            .Select(x => $"{x.ProductName} ({x.Sku}, {x.Size}): pedido {x.Quantity}, reservado {x.ReservedQuantity}.").ToArray();
        if (items.Count == 0 || differences.Length > 0)
        {
            var detail = items.Count == 0 ? "El pedido no contiene artículos reservados." : string.Join(" ", differences);
            if (detail.Length > 3900) detail = detail[..3900];
            var alert = await db.InventoryAlerts.SingleOrDefaultAsync(x => x.OrderId == id, ct);
            var notify = alert is null || alert.ResolvedAtUtc is not null || alert.Detail != detail;
            if (alert is null) { alert = new() { OrderId = id }; db.InventoryAlerts.Add(alert); }
            alert.Detail = detail; alert.RaisedAtUtc = clock.GetUtcNow(); alert.ResolvedAtUtc = null;
            if (notify)
            {
                foreach (var email in await db.Users.Where(x => x.IsActive && x.RoleName == Roles.Administrator).Select(x => x.Email).ToListAsync(ct))
                    QueueOrderEmail(email, $"Revisar inventario del pedido {order.Number}", detail);
                Audit(actor, user, "order_inventory_alert", id, origin, "Rechazado");
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            throw InvalidFields("incomplete_reservation", "Reserva incompleta. El pedido conserva su estado; se generó una alerta para Administración.",
                new() { ["Items"] = differences.Length > 0 ? differences : [detail] }, 409);
        }
        var currentAlert = await db.InventoryAlerts.SingleOrDefaultAsync(x => x.OrderId == id && x.ResolvedAtUtc == null, ct);
        if (currentAlert is not null) currentAlert.ResolvedAtUtc = clock.GetUtcNow();
        order.PreparationUserId = user.Id; order.PreparationUserName = user.Name; order.PreparationStartedAtUtc = clock.GetUtcNow();
        if (order.Status == OrderStatuses.Confirmed) ChangeStatus(order, OrderStatuses.Preparing, user);
        else order.UpdatedAtUtc = clock.GetUtcNow();
        Audit(actor, user, "order_preparation_start", id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }

    public async Task<OrderView> CorrectReservationsAsync(Actor actor, Guid id, ReservationCorrectionRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", "ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct);
        if (user.RoleName != Roles.Administrator) throw new SecurityFault("forbidden", "Solo Administración puede corregir las reservas.", 403);
        var order = await GetOrder(id, ct);
        if (order.Status != OrderStatuses.Confirmed) throw new SecurityFault("invalid_transition", "Solo puedes corregir reservas de un pedido Confirmado.", 409);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
            throw InvalidFields("reason_required", "Indica el motivo de la corrección.", new() { ["Reason"] = ["Escribe un motivo de 5 a 500 caracteres."] });
        await RequirePaid(order, ct);
        var items = await db.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        var quantities = Quantities(request.Items, items);
        if (items.Any(x => quantities[x.Id] != x.Quantity || x.ReservedQuantity < 0))
            throw InvalidFields("invalid_reservation", "La reserva corregida debe coincidir con cada cantidad comprada.", new() { ["Items"] = ["Revisa todas las cantidades."] });
        foreach (var group in items.GroupBy(x => x.ProductId))
        {
            var product = await db.BaseProducts.SingleAsync(x => x.Id == group.Key, ct);
            var delta = group.Sum(x => x.Quantity - x.ReservedQuantity);
            if (product.StockQuantity - delta is < 0 or > 100000)
                throw new SecurityFault("insufficient_stock", $"No hay existencias disponibles para corregir la reserva de {product.Name}.", 409);
            product.StockQuantity -= delta; product.Revision = Guid.NewGuid(); product.UpdatedAtUtc = clock.GetUtcNow();
        }
        foreach (var item in items) item.ReservedQuantity = item.Quantity;
        var alert = await db.InventoryAlerts.SingleOrDefaultAsync(x => x.OrderId == id, ct);
        if (alert is not null) alert.ResolvedAtUtc = clock.GetUtcNow();
        Audit(actor, user, "order_reservation_correction", id, origin);
        db.Events.Local.Last().Detail = request.Reason.Trim();
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }

    public async Task<OrderView> SavePackingAsync(Actor actor, Guid id, PackingRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct); var order = await GetOrder(id, ct);
        if (order.Status != OrderStatuses.Preparing) throw new SecurityFault("invalid_transition", "Verifica y empaca los artículos durante En preparación.", 409);
        Responsible(order, user);
        var items = await db.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        var values = Quantities(request.Items, items);
        if (items.Any(x => x.ReservedQuantity != x.Quantity || values[x.Id] > x.Quantity))
            throw InvalidFields("invalid_packing", "Las cantidades empacadas deben estar dentro de la reserva completa del pedido.", new() { ["Items"] = ["Revisa las cantidades reservadas y empacadas."] });
        if (items.Any(x => x.PackedQuantity != values[x.Id]))
        {
            foreach (var item in items) item.PackedQuantity = values[item.Id];
            order.UpdatedAtUtc = clock.GetUtcNow(); Audit(actor, user, "order_packing_verified", id, origin);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return await View(order, ct);
    }

    public async Task<OrderView> DispatchAsync(Actor actor, Guid id, DispatchRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct); var order = await GetOrder(id, ct);
        var carrier = request.Carrier?.Trim() ?? ""; var tracking = request.TrackingNumber?.Trim() ?? "";
        var normalized = tracking.ToUpperInvariant();
        if (order.Status == OrderStatuses.Shipped && order.Carrier == carrier && order.NormalizedTrackingNumber == normalized)
        { Responsible(order, user); await tx.CommitAsync(ct); return await View(order, ct); }
        if (order.Status != OrderStatuses.Preparing)
            throw new SecurityFault("invalid_transition", "El pedido debe estar En preparación y tener todos sus artículos verificados y empacados antes del despacho.", 409);
        Responsible(order, user);
        var errors = new Dictionary<string, string[]>();
        if (carrier.Length is < 1 or > 100) errors["Carrier"] = ["Indica el transportista (máximo 100 caracteres)."]; 
        if (tracking.Length is < 1 or > 100) errors["TrackingNumber"] = ["Indica el número de guía (máximo 100 caracteres)."]; 
        if (errors.Count > 0) throw InvalidFields("missing_tracking", "Completa los datos de despacho.", errors);
        if (await db.Orders.AnyAsync(x => x.Id != id && (x.NormalizedTrackingNumber == normalized || x.TrackingNumber.ToUpper() == normalized), ct))
            throw InvalidFields("duplicate_tracking", "Ese número de guía ya está registrado en otro pedido.", new() { ["TrackingNumber"] = ["El número de guía debe ser único."] }, 409);
        var items = await db.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        if (items.Count == 0 || items.Any(x => x.Quantity <= 0 || x.ReservedQuantity != x.Quantity || x.PackedQuantity != x.Quantity))
            throw InvalidFields("packing_required", "Verifica y empaca todos los artículos antes de marcar En camino.", new() { ["Items"] = ["Hay artículos pendientes de verificar o empacar."] }, 409);
        await RequirePaid(order, ct);
        order.Carrier = carrier; order.TrackingNumber = tracking; order.NormalizedTrackingNumber = normalized;
        order.DispatchedAtUtc = clock.GetUtcNow(); ChangeStatus(order, OrderStatuses.Shipped, user);
        var email = await db.Users.Where(x => x.Id == order.OwnerUserId).Select(x => x.Email).SingleAsync(ct);
        QueueOrderEmail(email, $"Tu pedido {order.Number} está En camino", $"Transportista: {carrier}\nGuía: {tracking}\nDespacho: {order.DispatchedAtUtc:O}\nConsulta Mis pedidos para ver el seguimiento.");
        Audit(actor, user, "order_dispatch", id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }

    public async Task<OrderView> DeliverAsync(Actor actor, Guid id, DeliveryRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct); var order = await GetOrder(id, ct);
        if (order.Status == OrderStatuses.Delivered) { await tx.CommitAsync(ct); return await View(order, ct); }
        if (order.Status != OrderStatuses.Shipped) throw new SecurityFault("invalid_transition", "El pedido debe estar En camino para confirmar su entrega.", 409);
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.CarrierConfirmation) || request.CarrierConfirmation.Trim().Length > 200)
            errors["CarrierConfirmation"] = ["Indica la referencia de confirmación del transportista."];
        if (string.IsNullOrWhiteSpace(request.ReceiptReference) || request.ReceiptReference.Trim().Length > 200)
            errors["ReceiptReference"] = ["Indica la referencia de recepción."];
        if (request.DeliveredAtUtc is null || request.DeliveredAtUtc > clock.GetUtcNow().AddMinutes(1) ||
            request.DeliveredAtUtc < (order.DispatchedAtUtc ?? order.CreatedAtUtc))
            errors["DeliveredAtUtc"] = ["Indica fecha y hora de entrega entre el despacho y el momento actual."];
        if (errors.Count > 0) throw InvalidFields("invalid_delivery", "Completa la confirmación de entrega.", errors);
        var items = await db.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        var values = Quantities(request.Items, items);
        var missing = items.Where(x => values[x.Id] != x.Quantity).Select(x => $"{x.ProductName} ({x.Sku}): recibido {values[x.Id]}, esperado {x.Quantity}.").ToArray();
        if (items.Count == 0 || missing.Length > 0)
            throw InvalidFields("incomplete_delivery", "No se permiten entregas parciales. Registra una incidencia con los artículos faltantes.", new() { ["Items"] = missing.Length > 0 ? missing : ["El pedido no contiene artículos."] }, 409);
        order.DeliveredAtUtc = request.DeliveredAtUtc!.Value.ToUniversalTime(); order.DeliveryRecordedAtUtc = clock.GetUtcNow();
        order.DeliveryUserId = user.Id; order.DeliveryUserName = user.Name; order.ReceiptReference = request.ReceiptReference.Trim();
        order.CarrierConfirmation = request.CarrierConfirmation.Trim(); order.PointsEnabled = true; order.RatingEnabled = true;
        ChangeStatus(order, OrderStatuses.Delivered, user); Audit(actor, user, "order_delivery", id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }

    public async Task<OrderView> RecordIncidentAsync(Actor actor, Guid id, OrderIncidentRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct); var order = await GetOrder(id, ct);
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Detail) || request.Detail.Trim().Length is < 5 or > 1000)
            throw InvalidFields("invalid_incident", "Describe la incidencia (5 a 1000 caracteres).", new() { ["Detail"] = ["La descripción es obligatoria."] });
        if (await db.OrderIncidents.AnyAsync(x => x.OrderId == id && x.RequestId == request.RequestId, ct))
        { await tx.CommitAsync(ct); return await View(order, ct); }
        if (order.Status != OrderStatuses.Shipped) throw new SecurityFault("invalid_transition", "Registra incidencias de entrega mientras el pedido esté En camino.", 409);
        var items = await db.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        var missing = request.MissingItems ?? [];
        if (missing.Length == 0 || missing.Any(x => x is null || !items.Any(item => item.Id == x.ItemId && x.Quantity > 0 && x.Quantity <= item.Quantity)) || missing.Select(x => x.ItemId).Distinct().Count() != missing.Length)
            throw InvalidFields("invalid_items", "Identifica los artículos faltantes y sus cantidades.", new() { ["Items"] = ["Selecciona al menos un artículo faltante sin duplicarlo."] });
        db.OrderIncidents.Add(new OrderIncident { OrderId = id, RequestId = request.RequestId, Detail = request.Detail.Trim(),
            ReportedByUserId = user.Id, ReportedByName = user.Name, OccurredAtUtc = clock.GetUtcNow(),
            MissingItemsJson = JsonSerializer.Serialize(missing.Select(x => new { x.ItemId, ProductName = items.Single(i => i.Id == x.ItemId).ProductName, x.Quantity })) });
        Audit(actor, user, "order_delivery_incident", id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }

    private async Task<OrderView> CancelOrderAsync(Actor actor, Guid id, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", "ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Staff(actor, ct); var order = await GetOrder(id, ct);
        if (order.Status == OrderStatuses.Cancelled) { await tx.CommitAsync(ct); return await View(order, ct); }
        if (order.Status is not (OrderStatuses.Confirmed or OrderStatuses.Preparing))
            throw new SecurityFault("invalid_transition", "Solo puedes cancelar un pedido antes de su despacho.", 409);
        var items = await db.OrderItems.Where(x => x.OrderId == id).ToListAsync(ct);
        foreach (var group in items.GroupBy(x => x.ProductId))
        {
            var product = await db.BaseProducts.SingleAsync(x => x.Id == group.Key, ct);
            if (group.Any(x => x.ReservedQuantity < 0 || x.ReservedQuantity > x.Quantity))
                throw new SecurityFault("invalid_reservation", "Revisa las reservas antes de cancelar el pedido.", 409);
            product.StockQuantity += group.Sum(x => x.ReservedQuantity); product.Revision = Guid.NewGuid(); product.UpdatedAtUtc = clock.GetUtcNow();
            foreach (var item in group) { item.ReservedQuantity = 0; item.PackedQuantity = 0; }
        }
        ChangeStatus(order, OrderStatuses.Cancelled, user); Audit(actor, user, "order_cancel", id, origin);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await View(order, ct);
    }

    public async Task<PurchaseRatingView> RateAsync(Actor actor, Guid id, PurchaseRatingRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:orders", SqlSecurityTransaction.User(actor.UserId)], ct);
        var user = await Client(actor, ct);
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == user.Id, ct)
            ?? throw new SecurityFault("order_not_found", "Pedido no encontrado.", 404);
        if (order.Status != OrderStatuses.Delivered || !order.RatingEnabled)
            throw new SecurityFault("delivery_required", "Podrás calificar la compra cuando se confirme su entrega.", 409);
        if (request.Stars is < 1 or > 5 || (request.Comment?.Length ?? 0) > 1000)
            throw new SecurityFault("invalid_rating", "Selecciona entre 1 y 5 estrellas y un comentario de hasta 1000 caracteres.");
        var rating = await db.PurchaseRatings.SingleOrDefaultAsync(x => x.OrderId == id, ct);
        if (rating is null)
        {
            rating = new() { OrderId = id, Stars = request.Stars, Comment = request.Comment?.Trim() ?? "", CreatedAtUtc = clock.GetUtcNow() };
            db.PurchaseRatings.Add(rating); Audit(actor, user, "order_rating", id, origin); await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return new(rating.Stars, rating.Comment, rating.CreatedAtUtc);
    }
}
