namespace GoalFueled.Data.Repositories;

/// <summary>A tiny persisted key/value store for runtime-adjustable application settings.</summary>
public interface IAppSettingsRepository
{
    /// <summary>Returns the value for <paramref name="key"/>, or null if unset.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Creates or updates the value for <paramref name="key"/>.</summary>
    Task SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Removes the setting for <paramref name="key"/> (no-op if absent).</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
