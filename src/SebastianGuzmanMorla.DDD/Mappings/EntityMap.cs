using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace SebastianGuzmanMorla.DDD.Mappings;

public static class EntityMap
{
    public static void ConfigureEntity<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : Entity
    {
        builder
            .HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnOrder(0)
            .HasColumnName(nameof(Entity.Id))
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName(nameof(Entity.CreatedAt))
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName(nameof(Entity.UpdatedAt))
            .IsRequired();

        builder.Property(x => x.DeletedAt)
            .HasColumnName(nameof(Entity.DeletedAt))
            .IsRequired(false);

        builder
            .HasIndex(x => x.UpdatedAt)
            .IsDescending();

        builder
            .HasIndex(x => x.DeletedAt)
            .IsDescending();
    }
}
