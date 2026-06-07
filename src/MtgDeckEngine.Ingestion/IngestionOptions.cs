namespace MtgDeckEngine.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Commander slugs to ingest from EDHREC.</summary>
    public string[] Commanders { get; set; } = ["xyris-the-writhing-storm"];

    /// <summary>Skip running ingestion at startup (e.g. for unit tests).</summary>
    public bool DisableStartupRun { get; set; }

    /// <summary>Inter-card delay for Scryfall lookups in ms.</summary>
    public int ScryfallDelayMs { get; set; } = 100;

    /// <summary>Max cards to resolve per commander (cap for Phase 1).</summary>
    public int MaxCardsPerCommander { get; set; } = 250;

    /// <summary>Enable EDHTop16 tournament data ingestion (Phase 2).</summary>
    public bool EnableEdhTop16 { get; set; } = true;

    /// <summary>Max tournament entries per commander to ingest from EDHTop16.</summary>
    public int MaxTournamentEntriesPerCommander { get; set; } = 100;
}
