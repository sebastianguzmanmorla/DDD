using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace SebastianGuzmanMorla.DDD.Mappings;

public class LogMap : IEntityTypeConfiguration<Log>
{
    public void Configure(EntityTypeBuilder<Log> builder)
    {
        builder.ToTable(nameof(Log));

        builder.Property(x => x.LogRequestId)
            .HasColumnOrder(1)
            .HasColumnName(nameof(Log.LogRequestId))
            .IsRequired(false);

        builder.Property(x => x.Type)
            .HasColumnOrder(2)
            .HasColumnName(nameof(Log.Type))
            .IsRequired();

        builder.Property(x => x.Message)
            .HasColumnOrder(3)
            .HasColumnName(nameof(Log.Message))
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.ReferenceType)
            .HasColumnOrder(4)
            .HasColumnName(nameof(Log.ReferenceType))
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.ReferenceId)
            .HasColumnOrder(5)
            .HasColumnName(nameof(Log.ReferenceId));

        builder.Property(x => x.ReferenceData)
            .HasColumnOrder(6)
            .HasColumnName(nameof(Log.ReferenceData))
            .HasColumnType("jsonb");

        builder.ConfigureEntity();

        builder.HasIndex(x => x.LogRequestId);

        builder.HasIndex(x => x.Type);

        builder.HasIndex(x => x.ReferenceType);

        builder.HasIndex(x => x.ReferenceId);

        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
    }
}
