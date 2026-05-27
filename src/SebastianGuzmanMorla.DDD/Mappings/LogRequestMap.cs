using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace SebastianGuzmanMorla.DDD.Mappings;

public class LogRequestMap : IEntityTypeConfiguration<LogRequest>
{
    public void Configure(EntityTypeBuilder<LogRequest> builder)
    {
        builder.ToTable(nameof(LogRequest));

        builder.Property(x => x.Context)
            .HasColumnOrder(1)
            .HasColumnName(nameof(LogRequest.Context))
            .HasColumnType("jsonb");

        builder.Property(x => x.Type)
            .HasColumnOrder(2)
            .HasColumnName(nameof(LogRequest.Type))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Request)
            .HasColumnOrder(3)
            .HasColumnName(nameof(LogRequest.Request))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.ConfigureEntity();

        builder.HasIndex(x => x.Type);
    }
}
