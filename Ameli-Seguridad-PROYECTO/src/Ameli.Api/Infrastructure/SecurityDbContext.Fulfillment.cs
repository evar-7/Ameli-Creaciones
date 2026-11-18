using Ameli.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Infrastructure;

public sealed partial class SecurityDbContext
{
    public DbSet<OrderInventoryAlert> InventoryAlerts => Set<OrderInventoryAlert>();
    public DbSet<OrderIncident> OrderIncidents => Set<OrderIncident>();
    public DbSet<PurchaseRating> PurchaseRatings => Set<PurchaseRating>();

    private static void ConfigureFulfillment(ModelBuilder b)
    {
        b.Entity<ProductCategory>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<CustomerOrder>(e =>
        {
            e.Property(x => x.PaymentStatus).HasMaxLength(40);
            e.Property(x => x.PreparationUserName).HasMaxLength(160);
            e.Property(x => x.DeliveryUserName).HasMaxLength(160);
            e.Property(x => x.ReceiptReference).HasMaxLength(200);
            e.Property(x => x.CarrierConfirmation).HasMaxLength(200);
            e.Property(x => x.NormalizedTrackingNumber).HasMaxLength(100);
            e.HasIndex(x => x.NormalizedTrackingNumber).IsUnique().HasFilter("[normalized_tracking_number] IS NOT NULL");
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PreparationUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.DeliveryUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OrderStatusHistory>().Property(x => x.ActorName).HasMaxLength(160);
        b.Entity<OrderInventoryAlert>(e =>
        {
            e.ToTable("order_inventory_alerts"); e.HasKey(x => x.OrderId);
            e.Property(x => x.Detail).HasMaxLength(4000);
            e.HasOne<CustomerOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OrderIncident>(e =>
        {
            e.ToTable("order_incidents"); e.HasKey(x => x.Id);
            e.Property(x => x.ReportedByName).HasMaxLength(160);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.MissingItemsJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.OrderId, x.RequestId }).IsUnique();
            e.HasOne<CustomerOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PurchaseRating>(e =>
        {
            e.ToTable("purchase_ratings"); e.HasKey(x => x.OrderId);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasOne<CustomerOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
