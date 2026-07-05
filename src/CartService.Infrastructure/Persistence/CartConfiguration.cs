using CartService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CartService.Infrastructure.Persistence;

/// <summary>
/// Cart entity configuration.
/// Uses PostgreSQL xmin system column for optimistic concurrency.
/// </summary>
public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(c => c.OwnerId);

        // One active cart per authenticated owner. The filter keeps guest carts
        // (OwnerId NULL) unconstrained, so any number of them may coexist.
        builder.HasIndex(c => c.OwnerId)
            .IsUnique()
            .HasFilter("\"OwnerId\" IS NOT NULL");

        // Optimistic concurrency token (PostgreSQL xmin system column)
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey(ci => ci.CartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
