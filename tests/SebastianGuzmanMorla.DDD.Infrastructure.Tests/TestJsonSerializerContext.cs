using System.Text.Json.Serialization;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestEntity))]
[JsonSerializable(typeof(List<TestEntity>))]
public partial class TestJsonSerializerContext : JsonSerializerContext;
