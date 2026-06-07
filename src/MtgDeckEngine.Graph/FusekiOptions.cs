namespace MtgDeckEngine.Graph;

/// <summary>
/// Configuration for the SPARQL 1.1 endpoint the repository talks to.
/// Named "Fuseki" for the local-dev case but the same options drive every
/// SPARQL 1.1 store — Neptune, GraphDB, Stardog, Virtuoso, etc.
///
/// <para>
/// <b>Neptune (AWS, Phase 5):</b>
/// <list type="bullet">
///   <item><c>QueryEndpoint = https://&lt;cluster-id&gt;.&lt;region&gt;.neptune.amazonaws.com:8182/sparql</c></item>
///   <item><c>UpdateEndpoint = https://&lt;cluster-id&gt;.&lt;region&gt;.neptune.amazonaws.com:8182/sparql</c> (same path)</item>
///   <item>From inside an EKS pod with IRSA + VPC access, no signing is needed.</item>
///   <item>From outside the VPC, requests must be SigV4-signed —
///         enable <see cref="UseSigV4Signing"/> and configure the SDK credential chain
///         (env vars, IAM role, profile).</item>
/// </list>
/// </para>
/// </summary>
public sealed class FusekiOptions
{
    public const string SectionName = "Fuseki";

    /// <summary>SPARQL Query endpoint, e.g. http://localhost:3030/mtg/sparql</summary>
    public string QueryEndpoint { get; set; } = "http://localhost:3030/mtg/sparql";

    /// <summary>SPARQL Update endpoint, e.g. http://localhost:3030/mtg/update</summary>
    public string UpdateEndpoint { get; set; } = "http://localhost:3030/mtg/update";

    /// <summary>
    /// When true, all HTTP requests to the endpoint are signed with AWS
    /// SigV4 before send-off — required for Neptune calls that originate
    /// outside its VPC. Implemented by <c>NeptuneSigV4Handler</c> when present.
    /// </summary>
    public bool UseSigV4Signing { get; set; }

    /// <summary>AWS region for SigV4 signing (only used when <see cref="UseSigV4Signing"/> is true).</summary>
    public string? AwsRegion { get; set; }
}
