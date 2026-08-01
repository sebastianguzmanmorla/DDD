using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Converters;

public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
