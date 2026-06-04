using System.Text.Json.Serialization;

namespace SebastianGuzmanMorla.DDD.Domain.Models;

public class HealthCheckEntryModel
{
    [JsonPropertyName("data")]
    public IReadOnlyDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("duration")] public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tags")] public IEnumerable<string> Tags { get; set; } = new List<string>();
}
