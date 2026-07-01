namespace GoalFueled.Api.Services.OpenFoodFacts;

/// <summary>
/// Abstraction over the Open Food Facts API. Implemented by <see cref="OffClient"/> in production;
/// faked in tests so the barcode-lookup and contribution logic can be exercised without any network.
/// </summary>
public interface IOffClient
{
    /// <summary>
    /// Fetches a product by barcode. Returns <c>null</c> when the barcode is unknown to OFF, the source
    /// is unreachable/rate-limited, or the body is unusable — callers treat all of these as "no match".
    /// </summary>
    Task<OffProduct?> GetProductAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// True when a write account is configured (user id + password). Contribution is skipped when false.
    /// </summary>
    bool CanContribute { get; }

    /// <summary>
    /// Writes a product to OFF via the legacy <c>product_jqm2.pl</c> endpoint. The caller supplies the
    /// product/nutriment form fields; this method adds the barcode, credentials and app identifiers.
    /// Returns <c>false</c> (never throws) on any failure so contribution degrades quietly.
    /// </summary>
    Task<bool> ContributeAsync(
        string barcode, IReadOnlyList<KeyValuePair<string, string>> fields, CancellationToken ct = default);
}
