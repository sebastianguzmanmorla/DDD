using System.Text.Json.Serialization;

namespace SebastianGuzmanMorla.DDD.Domain.Messaging;

public class HealthCheckReportModel
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    [JsonPropertyName("totalDuration")] public string TotalDuration { get; set; } = string.Empty;

    [JsonPropertyName("entries")] public Dictionary<string, HealthCheckEntryModel> Entries { get; set; } = new();
}
