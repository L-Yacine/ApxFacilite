using APXEMI.Models;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Data;

public class EmiDbContext(DbContextOptions<EmiDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<InstalmentPlan> InstalmentPlans => Set<InstalmentPlan>();

    public DbSet<SaleLine> SaleLines => Set<SaleLine>();

    public DbSet<Instalment> Instalments => Set<Instalment>();

    public DbSet<PrelevementBatch> PrelevementBatches => Set<PrelevementBatch>();

    public DbSet<PrelevementLine> PrelevementLines => Set<PrelevementLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Username).UseCollation("NOCASE");
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        // Singleton fixe (Id = 1) : l'identité SQL doit être désactivée pour
        // permettre l'insertion explicite de SingletonId (voir SettingsController).
        modelBuilder.Entity<StoreSettings>(entity =>
        {
            entity.Property(s => s.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(c => c.Name).UseCollation("NOCASE");
            entity.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.Property(b => b.Name).UseCollation("NOCASE");
            entity.HasIndex(b => b.Name).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name).UseCollation("NOCASE");
            entity.Property(p => p.UnitCost).HasPrecision(18, 2);
            entity.Property(p => p.UnitSalePrice).HasPrecision(18, 2);
            entity.HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(p => p.Brand)
                .WithMany()
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.Property(s => s.Name).UseCollation("NOCASE");
            entity.HasIndex(s => s.Name).IsUnique();
        });

        modelBuilder.Entity<Purchase>(entity =>
        {
            entity.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(p => p.Supplier)
                .WithMany(s => s.Purchases)
                .HasForeignKey(p => p.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseLine>(entity =>
        {
            entity.Property(l => l.UnitCost).HasPrecision(18, 2);
            entity.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
            entity.HasOne(m => m.Product)
                .WithMany()
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(m => m.Purchase)
                .WithMany()
                .HasForeignKey(m => m.PurchaseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(m => m.InstalmentPlan)
                .WithMany()
                .HasForeignKey(m => m.InstalmentPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InstalmentPlan>(entity =>
        {
            entity.Property(p => p.TotalAmount).HasPrecision(18, 2);
            entity.Property(p => p.DownPayment).HasPrecision(18, 2);
            entity.Property(p => p.InstalmentAmount).HasPrecision(18, 2);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(p => p.Client)
                .WithMany(c => c.Plans)
                .HasForeignKey(p => p.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleLine>(entity =>
        {
            entity.Property(l => l.UnitPrice).HasPrecision(18, 2);
            entity.HasOne(l => l.InstalmentPlan)
                .WithMany(p => p.Lines)
                .HasForeignKey(l => l.InstalmentPlanId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Instalment>(entity =>
        {
            entity.Property(i => i.Amount).HasPrecision(18, 2);
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(i => i.InstalmentPlan)
                .WithMany(p => p.Instalments)
                .HasForeignKey(i => i.InstalmentPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrelevementBatch>(entity =>
        {
            entity.HasIndex(b => b.ReferenceMonth).IsUnique();
        });

        modelBuilder.Entity<PrelevementLine>(entity =>
        {
            entity.Property(l => l.Amount).HasPrecision(18, 2);
            entity.Property(l => l.ReconcileStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(InstalmentStatus.Pending);
            // Une mensualité ne peut être « en attente » que dans UN seul lot ;
            // une fois rapprochée (Payée/Échouée), elle peut réapparaître dans
            // un lot ultérieur — c'est le report automatique des échecs (#10).
            entity.HasIndex(l => l.InstalmentId)
                .IsUnique()
                .HasFilter("[ReconcileStatus] = 'Pending'");
            entity.HasOne(l => l.Batch)
                .WithMany(b => b.Lines)
                .HasForeignKey(l => l.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(l => l.Instalment)
                .WithMany()
                .HasForeignKey(l => l.InstalmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
