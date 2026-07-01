using GoalFueled.Api.Common;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Data.Repositories;
using Microsoft.Extensions.Configuration;

namespace GoalFueled.Tests;

/// <summary>
/// Exercises the admin OCR-model override: precedence over config, persistence, validation, and clear.
/// </summary>
public class VisionModelSettingsServiceTests
{
    private sealed class FakeSettings : IAppSettingsRepository
    {
        private readonly Dictionary<string, string> _store = new();
        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Fact]
    public async Task Defaults_to_builtin_model_when_nothing_configured()
    {
        var svc = new VisionModelSettingsService(new FakeSettings(), Config());

        var dto = await svc.GetAsync();

        Assert.Equal(AnthropicVisionClient.DefaultModel, dto.ConfiguredModel);
        Assert.False(dto.IsModelConfigured);
        Assert.Null(dto.OverrideModel);
        Assert.Equal(AnthropicVisionClient.DefaultModel, dto.EffectiveModel);
        Assert.False(dto.IsApiKeyConfigured);
        Assert.Contains("claude-sonnet-5", dto.AvailableModels);
    }

    [Fact]
    public async Task Reflects_configured_model_and_api_key()
    {
        var svc = new VisionModelSettingsService(new FakeSettings(), Config(
            ("GoalFueled:Anthropic:VisionModel", "claude-sonnet-5"),
            ("GoalFueled:Anthropic:ApiKey", "sk-test")));

        var dto = await svc.GetAsync();

        Assert.True(dto.IsModelConfigured);
        Assert.Equal("claude-sonnet-5", dto.ConfiguredModel);
        Assert.Equal("claude-sonnet-5", dto.EffectiveModel);
        Assert.True(dto.IsApiKeyConfigured);
    }

    [Fact]
    public async Task Override_wins_over_config_and_is_persisted()
    {
        var settings = new FakeSettings();
        var svc = new VisionModelSettingsService(settings, Config(
            ("GoalFueled:Anthropic:VisionModel", "claude-opus-4-8")));

        var afterSet = await svc.SetOverrideAsync("claude-haiku-4-5");

        Assert.Equal("claude-haiku-4-5", afterSet.OverrideModel);
        Assert.Equal("claude-haiku-4-5", afterSet.EffectiveModel);
        Assert.Equal("claude-opus-4-8", afterSet.ConfiguredModel); // config unchanged

        // Persisted: a fresh service instance sees the override.
        var reloaded = new VisionModelSettingsService(settings, Config());
        Assert.Equal("claude-haiku-4-5", await reloaded.GetOverrideAsync());
    }

    [Fact]
    public async Task Set_rejects_unknown_model()
    {
        var svc = new VisionModelSettingsService(new FakeSettings(), Config());

        await Assert.ThrowsAsync<ValidationException>(() => svc.SetOverrideAsync("gpt-4"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetOverrideAsync("  "));
    }

    [Fact]
    public async Task Clear_reverts_to_configured_model()
    {
        var settings = new FakeSettings();
        var svc = new VisionModelSettingsService(settings, Config(
            ("GoalFueled:Anthropic:VisionModel", "claude-opus-4-8")));
        await svc.SetOverrideAsync("claude-sonnet-5");

        await svc.ClearOverrideAsync();

        var dto = await svc.GetAsync();
        Assert.Null(dto.OverrideModel);
        Assert.Equal("claude-opus-4-8", dto.EffectiveModel);
    }
}
