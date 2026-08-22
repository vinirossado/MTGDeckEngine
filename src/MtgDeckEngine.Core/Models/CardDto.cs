namespace MtgDeckEngine.Core.Models;

public sealed record CardDto(
    string OracleId,
    string Name,
    IReadOnlyList<string> Colors,
    IReadOnlyList<string> ColorIdentity,
    string TypeLine,
    string? OracleText,
    decimal? PriceEur,
    decimal? PriceUsd,
    bool CommanderLegal,
    string? ImageUrl = null,
    bool IsGameChanger = false);
