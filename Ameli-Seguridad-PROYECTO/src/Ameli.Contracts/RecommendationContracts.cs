using System.ComponentModel.DataAnnotations;

namespace Ameli.Contracts;

public static class RecommendationConfidence
{
    public const string High = "Alta";
    public const string Medium = "Media";
    public const string Low = "Baja";
}

public sealed class RecommendationQuery
{
    [Required] public Guid? PetId { get; set; }
}

public sealed record RecommendationMissingField(string Field, string Label);

public sealed record RecommendationPetView(Guid Id, string Name, string Species, string Breed, Guid Revision,
    DateTimeOffset UpdatedAtUtc, DateTimeOffset? PreferencesUpdatedAtUtc, bool IsActive);

public sealed record RecommendationProductView(Guid ProductId, string Name, string CategoryName, string Icon,
    decimal UnitPrice, int StockQuantity, string SuggestedSize, string Confidence, string ContextToken,
    bool CanAddToCart, bool IsAvailable, string Reason);

public sealed record RecommendationAlternativeView(Guid ProductId, string Name, string CategoryName, string Icon,
    decimal UnitPrice, int StockQuantity, string SuggestedSize, string Confidence, bool CanAddToCart);

public sealed record RecommendationResultView(string Status, string Message, RecommendationPetView? Pet,
    List<RecommendationMissingField> MissingFields, List<RecommendationProductView> Items,
    List<RecommendationAlternativeView> Alternatives, DateTimeOffset GeneratedAtUtc, string ActionUrl, string ActionLabel);

public sealed class RecommendationExplanationQuery
{
    [Required] public Guid? PetId { get; set; }
    [Required] public Guid? ProductId { get; set; }
    [Required, MaxLength(256)] public string ContextToken { get; set; } = "";
}

public sealed record RecommendationExplanationView(Guid ProductId, string ProductName, string Confidence,
    bool IsStale, bool IsTemporarilyUnavailable, bool IsUnavailable, string Summary, List<string> Factors,
    List<string> MissingOptionalData, List<RecommendationAlternativeView> Alternatives,
    string RecalculateUrl, string RecalculateLabel);

public static class RecommendationFeedbackValue
{
    public const string Useful = "useful";
    public const string NotInterested = "not_interested";
}

public sealed class RecommendationFeedbackRequest
{
    [Required] public Guid? PetId { get; set; }
    [Required] public Guid? ProductId { get; set; }
    [Required, AllowedValues(RecommendationFeedbackValue.Useful, RecommendationFeedbackValue.NotInterested)] public string Value { get; set; } = "";
    [MaxLength(40)] public string ReasonCode { get; set; } = "";
    [MaxLength(300)] public string ReasonText { get; set; } = "";
}

public sealed record RecommendationFeedbackView(Guid PetId, Guid ProductId, string Value, string ReasonCode,
    string ReasonText, DateTimeOffset UpdatedAtUtc, bool ReplacedExisting);
