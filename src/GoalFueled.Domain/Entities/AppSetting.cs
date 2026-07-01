namespace GoalFueled.Domain.Entities;

/// <summary>
/// A single persisted application setting, keyed by <see cref="Id"/>. A tiny key/value store for
/// runtime-adjustable settings (e.g. an admin's temporary OCR-model override) that outlive a restart
/// without needing a config redeploy.
/// </summary>
public class AppSetting
{
    /// <summary>The setting key (also the document/partition id).</summary>
    public string Id { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
