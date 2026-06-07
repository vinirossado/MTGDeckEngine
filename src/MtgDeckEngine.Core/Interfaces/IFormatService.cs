using MtgDeckEngine.Core.Models;

namespace MtgDeckEngine.Core.Interfaces;

public interface IFormatService
{
    Task<IReadOnlyList<FormatSummary>> ListFormatsAsync(CancellationToken ct);
    Task<FormatMeta> GetFormatMetaAsync(string format, CancellationToken ct);
    Task<IReadOnlyList<FormatStaple>> GetFormatStaplesAsync(
        string format,
        decimal? maxPriceEur,
        int? maxPlacement,
        int minDeckCount,
        int limit,
        CancellationToken ct);
    Task<IReadOnlyList<CommanderSummary>> ListCommandersAsync(int limit, CancellationToken ct);
}
