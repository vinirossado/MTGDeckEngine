namespace MtgDeckEngine.Ingestion;

public sealed class TopDeckOptions
{
    public const string SectionName = "TopDeck";

    /// <summary>API key from your TopDeck.gg account profile.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Game name (TopDeck supports many TCGs; default to MTG).</summary>
    public string Game { get; set; } = "Magic: The Gathering";

    /// <summary>Formats to pull (case-sensitive per TopDeck's API).</summary>
    public string[] Formats { get; set; } = ["EDH", "Modern"];

    /// <summary>Sliding window: only pull tournaments from the last N days.</summary>
    public int LookbackDays { get; set; } = 60;

    /// <summary>Skip tournaments smaller than this (filters out tiny locals).</summary>
    public int MinParticipants { get; set; } = 16;
}
