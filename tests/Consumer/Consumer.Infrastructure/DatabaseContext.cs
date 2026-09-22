using Microsoft.EntityFrameworkCore;
using SebastianGuzmanMorla.DDD.Infrastructure.Converters;

namespace Consumer.Infrastructure;

public sealed class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder) => builder.ApplyGeneratedConfigurations();
}
