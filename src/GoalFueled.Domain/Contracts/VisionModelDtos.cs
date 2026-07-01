namespace GoalFueled.Domain.Contracts;

/// <summary>
/// The nutrition-label OCR vision-model configuration, for the admin tester. Lets an admin try different
/// models at runtime (an override that wins over config) before committing a choice to configuration.
/// </summary>
public sealed record VisionModelSettingsDto(
    /// <summary>The model configured via app settings (or the built-in default when none is set).</summary>
    string ConfiguredModel,
    /// <summary>True when a model is explicitly set in configuration (vs. relying on the built-in default).</summary>
    bool IsModelConfigured,
    /// <summary>The admin's runtime override, if any. Takes precedence over the configured model.</summary>
    string? OverrideModel,
    /// <summary>The model actually used for OCR right now (override if set, else configured).</summary>
    string EffectiveModel,
    /// <summary>True when an Anthropic API key is configured; without it, label OCR is disabled.</summary>
    bool IsApiKeyConfigured,
    /// <summary>The vision-capable models an admin may choose from.</summary>
    IReadOnlyList<string> AvailableModels);

/// <summary>Payload for setting the admin OCR-model override.</summary>
public sealed record VisionModelOverrideInput(string? Model);
