using Ameli.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Infrastructure;

public sealed partial class SecurityDbContext
{
    public DbSet<ProductSizeRule> ProductSizes => Set<ProductSizeRule>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<CartOperation> CartOperations => Set<CartOperation>();
    public DbSet<CustomerOrder> Orders => Set<CustomerOrder>();
    public DbSet<CustomerOrderItem> OrderItems => Set<CustomerOrderItem>();
    public DbSet<RecommendationFeedback> RecommendationFeedback => Set<RecommendationFeedback>();

    private static void ConfigureAssistant(ModelBuilder b)
    {
        b.Entity<BaseProduct>(e =>
        {
            e.Property(x => x.Sku).HasMaxLength(40).IsUnicode(false); e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.CareInstructions).HasMaxLength(1000);
            e.Property(x => x.FitNotes).HasMaxLength(500);
            e.Property(x => x.ColorsJson).HasMaxLength(2000);
            e.Property(x => x.GarmentType).HasMaxLength(40); e.Property(x => x.Style).HasMaxLength(40);
        });
        b.Entity<ProductSizeRule>(e =>
        {
            e.ToTable("product_size_rules"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(20); e.Property(x => x.Equivalent).HasMaxLength(80);
            e.Property(x => x.MinHeightCm).HasPrecision(6, 2); e.Property(x => x.MaxHeightCm).HasPrecision(6, 2);
            e.Property(x => x.MinWeightKg).HasPrecision(7, 2); e.Property(x => x.MaxWeightKg).HasPrecision(7, 2);
            e.HasIndex(x => new { x.ProductId, x.Name }).IsUnique();
            e.HasOne<BaseProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CartLine>(e =>
        {
            e.ToTable("cart_lines"); e.HasKey(x => x.Id); e.Property(x => x.Size).HasMaxLength(20);
            e.HasIndex(x => x.OwnerUserId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BaseProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PetProfile>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CartOperation>(e =>
        {
            e.ToTable("cart_operations"); e.HasKey(x => x.Id); e.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => new { x.OwnerUserId, x.RequestId }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CustomerOrder>(e =>
        {
            e.ToTable("customer_orders"); e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(40).IsUnicode(false); e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.OwnerUserId, x.CheckoutRequestId }).IsUnique();
            e.Property(x => x.Status).HasMaxLength(30); e.Property(x => x.Carrier).HasMaxLength(100);
            e.Property(x => x.TrackingNumber).HasMaxLength(100); e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.DeliverySnapshot).HasMaxLength(1200);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CustomerOrderItem>(e =>
        {
            e.ToTable("customer_order_items"); e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(160); e.Property(x => x.Sku).HasMaxLength(40).IsUnicode(false);
            e.Property(x => x.Size).HasMaxLength(20); e.Property(x => x.PetSnapshot).HasMaxLength(1200);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasOne<CustomerOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BaseProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PetProfile>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RecommendationFeedback>(e =>
        {
            e.ToTable("recommendation_feedback"); e.HasKey(x => x.Id);
            e.Property(x => x.Value).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ReasonCode).HasMaxLength(40).IsUnicode(false);
            e.Property(x => x.ReasonText).HasMaxLength(300);
            e.HasIndex(x => new { x.OwnerUserId, x.PetId, x.ProductId, x.SessionId }).IsUnique();
            e.HasIndex(x => new { x.OwnerUserId, x.PetId, x.UpdatedAtUtc });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PetProfile>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BaseProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
