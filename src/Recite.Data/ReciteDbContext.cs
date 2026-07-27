using Microsoft.EntityFrameworkCore;
using Recite.Core.Model;

namespace Recite.Data;

/// <summary>
/// EF Core context for a Recite library. Relational tables for entities and joins; the CSL
/// field bag rides along as a JSON <c>TEXT</c> column on <see cref="Item.FieldsJson"/>.
/// FTS5 is provisioned separately (see <see cref="LibraryInitializer"/>).
/// </summary>
public sealed class ReciteDbContext : DbContext
{
    public ReciteDbContext(DbContextOptions<ReciteDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<ExternalIdentifier> Identifiers => Set<ExternalIdentifier>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Contribution> Contributions => Set<Contribution>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Facet> Facets => Set<Facet>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<MergeLog> MergeLogs => Set<MergeLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Item>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
            e.HasIndex(x => x.Title);
            e.HasIndex(x => x.Year);
            e.HasIndex(x => x.CitationKey);
            e.Property(x => x.FieldsJson).HasColumnType("TEXT");
            e.Property(x => x.Type).HasMaxLength(64);

            e.HasOne(x => x.Journal).WithMany(j => j.Items)
                .HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Series).WithMany(s => s.Items)
                .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ContainerParent).WithMany(x => x.ContainerChildren)
                .HasForeignKey(x => x.ContainerParentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ExternalIdentifier>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Scheme, x.Value }).IsUnique();
            e.HasOne(x => x.Item).WithMany(i => i.Identifiers)
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Person>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
            e.HasIndex(x => x.UniqueKey).IsUnique();
        });

        b.Entity<Contribution>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ItemId, x.PersonId, x.Role }).IsUnique();
            e.HasOne(x => x.Item).WithMany(i => i.Contributions)
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Person).WithMany(p => p.Contributions)
                .HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Journal>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
            e.HasIndex(x => x.UniqueKey).IsUnique();
        });

        b.Entity<Series>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
            e.HasIndex(x => x.UniqueKey).IsUnique();
        });

        b.Entity<Facet>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
        });

        b.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
            e.HasOne(x => x.Facet).WithMany(f => f.Categories)
                .HasForeignKey(x => x.FacetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent).WithMany(c => c.Children)
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ItemCategory>(e =>
        {
            e.HasKey(x => new { x.ItemId, x.CategoryId });
            e.HasOne(x => x.Item).WithMany(i => i.Categories)
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Category).WithMany(c => c.Items)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Tag>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<ItemTag>(e =>
        {
            e.HasKey(x => new { x.ItemId, x.TagId });
            e.HasOne(x => x.Item).WithMany(i => i.Tags)
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany(t => t.Items)
                .HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Attachment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Item).WithMany(i => i.Attachments)
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<ProjectMember>(e =>
        {
            e.HasKey(x => new { x.ProjectId, x.ItemId });
            e.HasOne(x => x.Project).WithMany(p => p.Members)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Item).WithMany()
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MergeLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Uuid).IsUnique();
        });
    }
}
