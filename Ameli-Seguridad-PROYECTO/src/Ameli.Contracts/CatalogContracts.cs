using System.ComponentModel.DataAnnotations;

namespace Ameli.Contracts;

public static class ProductSpecies
{
    public const string Dog = "Perro";
    public const string Cat = "Gato";
    public const string All = "Todos";
    public static readonly string[] Options = [Dog, Cat, All];
}

public sealed class CategoryRequest
{
    [Required(ErrorMessage = "El nombre de la categoría es obligatorio."), StringLength(120, MinimumLength = 2)] public string Name { get; set; } = "";
    [StringLength(500)] public string Description { get; set; } = "";
    [Required] public bool? IsActive { get; set; }
}

public sealed class SupplierRequest
{
    [Required(ErrorMessage = "El nombre del proveedor es obligatorio."), StringLength(160, MinimumLength = 2)] public string Name { get; set; } = "";
    [StringLength(160)] public string ContactName { get; set; } = "";
    [Required(ErrorMessage = "El correo del proveedor es obligatorio."), EmailAddress(ErrorMessage = "Escribe un correo válido."), MaxLength(254)] public string Email { get; set; } = "";
    [Required, RegularExpression("^[0-9]{8}$", ErrorMessage = "El teléfono debe tener exactamente 8 dígitos.")] public string Phone { get; set; } = "";
    [StringLength(500)] public string Notes { get; set; } = "";
    [Required] public bool? IsActive { get; set; }
}

public sealed class BaseProductRequest
{
    [Required(ErrorMessage = "El nombre del producto es obligatorio."), StringLength(160, MinimumLength = 2)] public string Name { get; set; } = "";
    [StringLength(1000)] public string Description { get; set; } = "";
    [Required(ErrorMessage = "Selecciona una categoría.")] public Guid? CategoryId { get; set; }
    [Required(ErrorMessage = "Selecciona un proveedor.")] public Guid? SupplierId { get; set; }
    [Required, AllowedValues(ProductSpecies.Dog, ProductSpecies.Cat, ProductSpecies.All, ErrorMessage = "Selecciona un tipo válido.")] public string Species { get; set; } = "";
    [StringLength(80)] public string Material { get; set; } = "";
    [StringLength(8)] public string Icon { get; set; } = "";
    [Range(0.0, 99999999.99, ErrorMessage = "Indica un precio válido.")] public decimal UnitPrice { get; set; }
    [Range(0, 100000, ErrorMessage = "La existencia debe ser mayor o igual a cero.")] public int StockQuantity { get; set; }
    [Required] public bool? IsActive { get; set; }
}

public sealed class CategoryQuery
{
    [MaxLength(120)] public string? Search { get; set; }
    public bool? IsActive { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed class SupplierQuery
{
    [MaxLength(160)] public string? Search { get; set; }
    public bool? IsActive { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed class BaseProductQuery
{
    [MaxLength(160)] public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SupplierId { get; set; }
    public bool? IsActive { get; set; }
    [MaxLength(10)] public string? Species { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed record CategoryView(Guid Id, string Name, string Description, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record SupplierView(Guid Id, string Name, string ContactName, string Email, string Phone, string Notes, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record BaseProductView(Guid Id, string Name, string Description, Guid CategoryId, string CategoryName, Guid SupplierId, string SupplierName, string Species, string Material, string Icon, decimal UnitPrice, int StockQuantity, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record LookupItem(Guid Id, string Name, bool IsActive);
