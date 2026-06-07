using System.Text.Json.Serialization;

namespace MtgDeckEngine.Ingestion.Dto;

public sealed class ScryfallBulkDataIndex
{
    [JsonPropertyName("data")] public List<ScryfallBulkDataItem> Data { get; set; } = new();
}

public sealed class ScryfallBulkDataItem
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("download_uri")] public string DownloadUri { get; set; } = "";
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}
