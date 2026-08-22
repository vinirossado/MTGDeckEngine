using System.Text.Json.Serialization;

namespace MtgDeckEngine.Ingestion.Dto;

public sealed class ScryfallBulkDataIndex
{
    [JsonPropertyName("data")] public List<ScryfallBulkDataItem> Data { get; set; } = new();
}

public sealed class ScryfallBulkDataItem
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    // Scryfall retired the plain-JSON-array `download_uri` in favour of a
    // gzipped JSON-Lines file. Keep the old field so an older/mirrored
    // response still resolves, but prefer the JSONL one when present.
    [JsonPropertyName("download_uri")] public string DownloadUri { get; set; } = "";
    [JsonPropertyName("jsonl_download_uri")] public string JsonlDownloadUri { get; set; } = "";

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("compressed_size")] public long CompressedSize { get; set; }

    /// <summary>Download URL to use, and whether it is gzipped JSON Lines.</summary>
    public (string Uri, bool IsGzippedJsonl) ResolveDownload()
        => !string.IsNullOrWhiteSpace(JsonlDownloadUri)
            ? (JsonlDownloadUri, true)
            : (DownloadUri, false);

    public long ReportedSize => Size > 0 ? Size : CompressedSize;
}
