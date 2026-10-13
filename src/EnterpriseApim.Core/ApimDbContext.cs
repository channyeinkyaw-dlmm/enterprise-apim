using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.Core;

public sealed class ApimDbContext(DbContextOptions<ApimDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<GatewayCluster> Clusters => Set<GatewayCluster>();
    public DbSet<ClusterDestination> Destinations => Set<ClusterDestination>();
    public DbSet<GatewayRoute> Routes => Set<GatewayRoute>();
    public DbSet<Scope> Scopes => Set<Scope>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    /// <summary>Keys that encrypt admin session cookies, shared by all admin API instances.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            e.Property(t => t.Description).HasMaxLength(1000);
            e.HasIndex(t => t.Name).IsUnique();
        });

        b.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(k => k.Id);
            e.Property(k => k.KeyId).HasMaxLength(32).IsRequired();
            e.Property(k => k.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(k => k.KeyId).IsUnique();
            e.HasIndex(k => k.TenantId);
            e.HasOne(k => k.Tenant)
                .WithMany(t => t.ApiKeys)
                .HasForeignKey(k => k.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GatewayCluster>(e =>
        {
            e.ToTable("clusters");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasMaxLength(100);
            e.Property(c => c.LoadBalancingPolicy).HasMaxLength(50);
            e.Property(c => c.HealthCheckPath).HasMaxLength(500);
            e.HasMany(c => c.Destinations)
                .WithOne()
                .HasForeignKey(d => d.ClusterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ClusterDestination>(e =>
        {
            e.ToTable("cluster_destinations");
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(100).IsRequired();
            e.Property(d => d.Address).HasMaxLength(2000).IsRequired();
            e.HasIndex(d => new { d.ClusterId, d.Name }).IsUnique();
        });

        b.Entity<GatewayRoute>(e =>
        {
            e.ToTable("routes");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasMaxLength(100);
            e.Property(r => r.Path).HasMaxLength(1000).IsRequired();
            e.Property(r => r.RequiredScope).HasMaxLength(100);
            e.Property(r => r.Description).HasMaxLength(1000);
            e.Property(r => r.TransformsJson).HasColumnType("jsonb");
            e.HasOne<GatewayCluster>()
                .WithMany()
                .HasForeignKey(r => r.ClusterId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Scope>()
                .WithMany()
                .HasForeignKey(r => r.RequiredScope)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Scope>(e =>
        {
            e.ToTable("scopes");
            e.HasKey(s => s.Name);
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Description).HasMaxLength(500);
        });

        b.Entity<AdminUser>(e =>
        {
            e.ToTable("admin_users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Username).HasMaxLength(100).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(u => u.SecurityStamp).HasMaxLength(100).IsRequired();
            e.HasIndex(u => u.Username).IsUnique();
        });

        b.Entity<DataProtectionKey>(e => e.ToTable("data_protection_keys"));
    }
}
