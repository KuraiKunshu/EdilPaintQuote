using EdilPaintPreventibiviGen.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Data;

public class AppDbContext : DbContext
{
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<CompanySettingsEntity> CompanySettings => Set<CompanySettingsEntity>();
    public DbSet<LaborCatalogEntity> LaborCatalog => Set<LaborCatalogEntity>();
    public DbSet<PersonalMaterialEntity> PersonalMaterials => Set<PersonalMaterialEntity>();
    public DbSet<QuoteEntity> Quotes => Set<QuoteEntity>();
    public DbSet<QuoteMaterialEntity> QuoteMaterials => Set<QuoteMaterialEntity>();
    public DbSet<QuoteLaborEntity> QuoteLabors => Set<QuoteLaborEntity>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CustomerEntity>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SyncId).IsUnique();

            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.SyncId).IsRequired();
            entity.Property(x => x.BusinessName).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.Email).HasMaxLength(250);
            entity.Property(x => x.Phone).HasMaxLength(100);
        });

        modelBuilder.Entity<CompanySettingsEntity>(entity =>
        {
            entity.ToTable("CompanySettings");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Nome).HasMaxLength(250);
            entity.Property(x => x.Indirizzo).HasMaxLength(500);
            entity.Property(x => x.Piva).HasMaxLength(50);
            entity.Property(x => x.Email).HasMaxLength(250);
            entity.Property(x => x.SelectedLogo).HasMaxLength(500);
            entity.Property(x => x.PaymentTerms).HasColumnName("TerminiPagamento");
        });

        modelBuilder.Entity<LaborCatalogEntity>(entity =>
        {
            entity.ToTable("LaborCatalog");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
        });

        modelBuilder.Entity<PersonalMaterialEntity>(entity =>
        {
            entity.ToTable("PersonalMaterials");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
        });

        modelBuilder.Entity<QuoteEntity>(entity =>
        {
            entity.ToTable("Quotes");
            entity.HasKey(x => x.Id);
            entity.HasQueryFilter(x => !x.IsDeleted);
            
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.HasIndex(x => x.QuoteNumber).IsUnique();
            entity.HasIndex(x => x.Date);

            entity.Property(x => x.QuoteNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PdfPath).HasMaxLength(1000);
            entity.Property(x => x.IvaType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CreatedByDevice).HasMaxLength(120);
            entity.Property(x => x.LastModifiedByDevice).HasMaxLength(120);
            entity.Property(x => x.SentMethod).HasMaxLength(80);
            entity.Property(x => x.SentRecipient).HasMaxLength(250);
            entity.Property(x => x.SentByDevice).HasMaxLength(120);
            entity.Property(x => x.LastReminderByDevice).HasMaxLength(120);

            entity.HasOne(x => x.Customer)
                .WithMany(x => x.QuotesAsCustomer)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ReferenceCustomer)
                .WithMany(x => x.QuotesAsReference)
                .HasForeignKey(x => x.ReferenceCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

        });

        modelBuilder.Entity<QuoteMaterialEntity>(entity =>
        {
            entity.ToTable("QuoteMaterials");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();

            entity.HasOne(x => x.Quote)
                .WithMany(x => x.Materials)
                .HasForeignKey(x => x.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuoteLaborEntity>(entity =>
        {
            entity.ToTable("QuoteLabors");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();

            entity.HasOne(x => x.Quote)
                .WithMany(x => x.Labors)
                .HasForeignKey(x => x.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
