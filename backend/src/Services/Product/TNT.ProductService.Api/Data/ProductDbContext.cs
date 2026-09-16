using Microsoft.EntityFrameworkCore;
using TNT.ProductService.Api.Entities;

namespace TNT.ProductService.Api.Data;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("Stores");
            entity.HasKey(store => store.Id);

            entity.Property(store => store.StoreCode).IsRequired().HasMaxLength(50);
            entity.Property(store => store.StoreName).IsRequired().HasMaxLength(200);
            entity.Property(store => store.AddressLine1).IsRequired().HasMaxLength(250);
            entity.Property(store => store.AddressLine2).HasMaxLength(250);
            entity.Property(store => store.City).IsRequired().HasMaxLength(100);
            entity.Property(store => store.PostalCode).IsRequired().HasMaxLength(30);
            entity.Property(store => store.Country).IsRequired().HasMaxLength(100);
            entity.Property(store => store.ContactNumber).IsRequired().HasMaxLength(30);
            entity.Property(store => store.Email).IsRequired().HasMaxLength(254);
            entity.Property(store => store.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(store => store.CreatedAtUtc).IsRequired();

            entity.HasIndex(store => store.StoreCode)
                .IsUnique()
                .HasDatabaseName("IX_Stores_StoreCode");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).IsRequired().HasMaxLength(100);
            entity.Property(category => category.Description).HasColumnType("text");
            entity.Property(category => category.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(category => category.CreatedAt).IsRequired();
            entity.Property(category => category.UpdatedAt);
            entity.HasIndex(category => category.Name).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", t =>
            {
                t.HasCheckConstraint("ck_products_stockquantity_nonnegative", "\"StockQuantity\" >= 0");
                t.HasCheckConstraint("ck_products_lowstockthreshold_nonnegative", "\"LowStockThreshold\" >= 0");
            });
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Name).IsRequired().HasMaxLength(200);
            entity.Property(product => product.Description).HasColumnType("text");
            entity.Property(product => product.Price).HasPrecision(10, 2);
            entity.Property(product => product.StockQuantity).IsRequired().HasDefaultValue(0);
            entity.Property(product => product.LowStockThreshold).IsRequired().HasDefaultValue(10);
            entity.Property(product => product.Unit).IsRequired().HasMaxLength(50).HasDefaultValue("item");
            entity.Property(product => product.ImageUrl).HasMaxLength(500);
            entity.Property(product => product.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(product => product.CreatedAtUtc).IsRequired();
            entity.HasIndex(product => product.CategoryId);
            entity.HasOne(product => product.Category)
                .WithMany()
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
