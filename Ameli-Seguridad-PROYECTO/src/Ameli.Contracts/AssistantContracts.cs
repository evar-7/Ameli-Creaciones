using System.ComponentModel.DataAnnotations;

namespace Ameli.Contracts;

public sealed class ProductSizeRequest
{
    public string Name { get; set; } = "";
    public decimal MinHeightCm { get; set; }
    public decimal MaxHeightCm { get; set; }
    public decimal MinWeightKg { get; set; }
    public decimal MaxWeightKg { get; set; }
    public string Equivalent { get; set; } = "";
}
public sealed record ProductSizeView(string Name, decimal MinHeightCm, decimal MaxHeightCm,
    decimal MinWeightKg, decimal MaxWeightKg, string Equivalent);

public sealed class ProductPresentationRequest
{
    public string Sku { get; set; } = "";
    public string Material { get; set; } = "";
    public string CareInstructions { get; set; } = "";
    public string FitNotes { get; set; } = "";
    public string[] Colors { get; set; } = [];
    public string GarmentType { get; set; } = "";
    public string Style { get; set; } = "";
    public ProductSizeRequest[] Sizes { get; set; } = [];
    public decimal UnitPrice { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
}
public sealed record StoreProductView(Guid Id, string Sku, string Name, string Description,
    Guid CategoryId, string CategoryName, string Species, string Material, string Icon,
    decimal UnitPrice, int StockQuantity, bool IsActive, string CareInstructions, string FitNotes,
    string[] Colors, string GarmentType, string Style, List<ProductSizeView> Sizes,
    DateTimeOffset UpdatedAtUtc, string ProductUrl, string GuideUrl);

public sealed class ChatRequest
{
    public Guid RequestId { get; set; }
    [MaxLength(1000)] public string Message { get; set; } = "";
    [AllowedValues("auto", "catalog", "size", "order", "recommendations")] public string Intent { get; set; } = "auto";
    public Guid? ProductId { get; set; }
    [MaxLength(80)] public string OrderNumber { get; set; } = "";
    public Guid? PetId { get; set; }
    [MaxLength(40)] public string Species { get; set; } = "";
    [MaxLength(100)] public string Breed { get; set; } = "";
    [MaxLength(40)] public string HeightCm { get; set; } = "";
    [MaxLength(40)] public string WeightKg { get; set; } = "";
}
public sealed record MissingChatField(string Field, string Label);
public sealed record SizeAdvice(string Status, string? SuggestedSize, string Message, List<ProductSizeView> Table,
    string FitNotes, string GuideUrl);
public sealed record ChatProductCard(StoreProductView Product, string? SuggestedSize, string Reason, Guid? PetId);
public sealed record ChatReply(Guid RequestId, string Kind, string Message, List<ChatProductCard> Products,
    List<MissingChatField> MissingFields, SizeAdvice? Size = null, OrderView? Order = null,
    string? ActionUrl = null, string? ActionLabel = null);

public sealed class AddCartRequest
{
    public Guid RequestId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? PetId { get; set; }
    [MaxLength(20)] public string Size { get; set; } = "";
    [Range(1, 20)] public int Quantity { get; set; } = 1;
}
public sealed record CartLineView(Guid Id, Guid ProductId, string ProductName, string Sku, string Size,
    Guid? PetId, string? PetName, int Quantity, decimal UnitPrice, int AvailableStock);
public sealed record CartView(List<CartLineView> Lines, decimal Total);
public sealed record CartAddResult(Guid RequestId, string Message, int Quantity);
public sealed class CheckoutRequest
{
    public Guid RequestId { get; set; }
    public Guid AddressId { get; set; }
}
public static class OrderStatuses
{
    public const string Confirmed = "Confirmado";
    public const string Preparing = "En preparación";
    public const string Shipped = "Enviado";
    public const string Delivered = "Entregado";
    public const string Cancelled = "Cancelado";
    public static readonly string[] All = [Confirmed, Preparing, Shipped, Delivered, Cancelled];
}
public sealed class OrderUpdateRequest
{
    [AllowedValues(OrderStatuses.Confirmed, OrderStatuses.Preparing, OrderStatuses.Shipped, OrderStatuses.Delivered, OrderStatuses.Cancelled)]
    public string Status { get; set; } = "";
    [MaxLength(100)] public string Carrier { get; set; } = "";
    [MaxLength(100)] public string TrackingNumber { get; set; } = "";
}
public sealed record OrderItemView(string ProductName, string Sku, string Size, int Quantity,
    decimal UnitPrice, string PetSnapshot);
public sealed record OrderView(Guid Id, string Number, string Status, DateTimeOffset UpdatedAtUtc,
    string Carrier, string TrackingNumber, DateTimeOffset CreatedAtUtc, decimal Total,
    string DeliverySnapshot, List<OrderItemView> Items);
