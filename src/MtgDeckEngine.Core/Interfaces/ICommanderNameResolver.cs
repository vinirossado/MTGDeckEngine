namespace MtgDeckEngine.Core.Interfaces;

/// <summary>
/// Slug → printed card name. Un-slugifying is lossy (commas and apostrophes are
/// dropped, so "kroxa-titan-of-deaths-hunger" cannot be rebuilt by string
/// surgery), which is why this goes through a real card index.
/// </summary>
public interface ICommanderNameResolver
{
    Task<string?> ResolveAsync(string commanderSlug, CancellationToken cancellationToken = default);
}
