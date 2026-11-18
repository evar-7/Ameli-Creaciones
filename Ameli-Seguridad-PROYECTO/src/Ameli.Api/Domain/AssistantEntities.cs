using Ameli.Contracts;

namespace Ameli.Api.Domain;

public sealed class ProductSizeRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string Name { get; set; } = "";
    public decimal MinHeightCm { get; set; }
    public decimal MaxHeightCm { get; set; }
    public decimal MinWeightKg { get; set; }
    public decimal MaxWeightKg { get; set; }
    public string Equivalent { get; set; } = "";
}
public sealed class CartLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? PetId { get; set; }
    public string Size { get; set; } = "";
    public int Quantity { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
public sealed class CartOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = "";
    public int ResultQuantity { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
public sealed class CustomerOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public Guid CheckoutRequestId { get; set; }
    public string Number { get; set; } = "";
    public string Status { get; set; } = OrderStatuses.Confirmed;
    public string Carrier { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public decimal Total { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public string PaymentMode { get; set; } = "";
    public string PaymentReference { get; set; } = "";
    public string DeliverySnapshot { get; set; } = "";
    public string PaymentStatus { get; set; } = "Pendiente de verificación";
    public Guid? PreparationUserId { get; set; }
    public string PreparationUserName { get; set; } = "";
    public DateTimeOffset? PreparationStartedAtUtc { get; set; }
    public DateTimeOffset? DispatchedAtUtc { get; set; }
    public string? NormalizedTrackingNumber { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? DeliveryRecordedAtUtc { get; set; }
    public Guid? DeliveryUserId { get; set; }
    public string DeliveryUserName { get; set; } = "";
    public string ReceiptReference { get; set; } = "";
    public string CarrierConfirmation { get; set; } = "";
    public bool PointsEnabled { get; set; }
    public bool RatingEnabled { get; set; }
}
public sealed class CustomerOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? PetId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Size { get; set; } = "";
    public int Quantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int PackedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string PetSnapshot { get; set; } = "";
}

public sealed class RecommendationFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public Guid PetId { get; set; }
    public Guid ProductId { get; set; }
    public Guid SessionId { get; set; }
    public string Value { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string ReasonText { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class OrderStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public Guid OrderId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Carrier { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = "";
}

public sealed class OrderInventoryAlert
{
    public Guid OrderId { get; set; }
    public string Detail { get; set; } = "";
    public DateTimeOffset RaisedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}
public sealed class OrderIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid RequestId { get; set; }
    public Guid ReportedByUserId { get; set; }
    public string ReportedByName { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Detail { get; set; } = "";
    public string MissingItemsJson { get; set; } = "[]";
}
public sealed class PurchaseRating
{
    public Guid OrderId { get; set; }
    public int Stars { get; set; }
    public string Comment { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
public sealed class PaymentAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public Guid RequestId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
}
