namespace MtgDeckEngine.Core.Interfaces;

public interface IIngestorService
{
    string SourceName { get; }
    Task IngestAsync(CancellationToken ct);
}
