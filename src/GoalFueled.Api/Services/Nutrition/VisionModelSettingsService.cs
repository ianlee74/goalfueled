using GoalFueled.Api.Common;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain.Contracts;
using Microsoft.Extensions.Configuration;

namespace GoalFueled.Api.Services.Nutrition;

/// <summary>
/// Manages the admin's runtime override for the nutrition-label OCR vision model. The override is
/// persisted (so it survives restarts while testing) and takes precedence over the configured model.
/// Once an admin settles on a model they can bake it into configuration and clear the override.
/// </summary>
public sealed class VisionModelSettingsService
{
    /// <summary>App-settings key under which the override is stored.</summary>
    public const string OverrideKey = "anthropic.visionModelOverride";

    /// <summary>Vision-capable Claude models an admin may pick from.</summary>
    public static readonly IReadOnlyList<string> AvailableModels = new[]
    {
        "claude-opus-4-8",
        "claude-opus-4-7",
        "claude-sonnet-5",
        "claude-haiku-4-5",
    };

    private readonly IAppSettingsRepository _settings;
    private readonly IConfiguration _config;

    public VisionModelSettingsService(IAppSettingsRepository settings, IConfiguration config)
    {
        _settings = settings;
        _config = config;
    }

    /// <summary>The persisted override model, or null when none is set.</summary>
    public async Task<string?> GetOverrideAsync(CancellationToken ct = default)
    {
        var value = await _settings.GetAsync(OverrideKey, ct);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Builds the full settings view for the admin UI.</summary>
    public async Task<VisionModelSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var over = await GetOverrideAsync(ct);
        var configured = _config["GoalFueled:Anthropic:VisionModel"];
        var isModelConfigured = !string.IsNullOrWhiteSpace(configured);
        var configuredModel = isModelConfigured ? configured!.Trim() : AnthropicVisionClient.DefaultModel;
        var effective = over ?? configuredModel;
        var apiKey = _config["GoalFueled:Anthropic:ApiKey"];

        return new VisionModelSettingsDto(
            ConfiguredModel: configuredModel,
            IsModelConfigured: isModelConfigured,
            OverrideModel: over,
            EffectiveModel: effective,
            IsApiKeyConfigured: !string.IsNullOrWhiteSpace(apiKey),
            AvailableModels: AvailableModels);
    }

    /// <summary>Sets the override to one of the <see cref="AvailableModels"/>. Throws on an unknown model.</summary>
    public async Task<VisionModelSettingsDto> SetOverrideAsync(string? model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("Choose a vision model.");
        var trimmed = model.Trim();
        if (!AvailableModels.Contains(trimmed))
            throw new ValidationException($"Unknown vision model '{trimmed}'.");

        await _settings.SetAsync(OverrideKey, trimmed, ct);
        return await GetAsync(ct);
    }

    /// <summary>Clears the override, reverting to the configured model.</summary>
    public async Task ClearOverrideAsync(CancellationToken ct = default) =>
        await _settings.RemoveAsync(OverrideKey, ct);
}
