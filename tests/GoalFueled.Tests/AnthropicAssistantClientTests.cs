using System.Net;
using System.Text;
using System.Text.Json;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services.Assistant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoalFueled.Tests;

/// <summary>
/// Pins the wire contract of the assistant's model client without touching the network: the outgoing
/// request shape (which this model family validates strictly — several once-common parameters are now
/// rejected outright with a 400), and the parsing of a realistic tool-use response.
/// </summary>
public class AnthropicAssistantClientTests
{
    /// <summary>Captures the outgoing request and replies with a canned response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static AnthropicAssistantClient NewClient(StubHandler handler, params (string, string)[] settings) =>
        new(new HttpClient(handler), Config(settings), NullLogger<AnthropicAssistantClient>.Instance);

    private static AssistantModelRequest SampleRequest() => new(
        "system prompt",
        AssistantToolCatalog.Definitions,
        new List<(string, JsonElement)>
        {
            ("user", JsonSerializer.SerializeToElement(new[] { new { type = "text", text = "I ate an apple" } })),
        });

    private const string ToolUseResponse = """
    {
      "id": "msg_1",
      "model": "claude-opus-5",
      "stop_reason": "tool_use",
      "content": [
        { "type": "text", "text": "Let me check your library." },
        { "type": "tool_use", "id": "toolu_1", "name": "search_fuel_sources", "input": { "query": "apple" } }
      ]
    }
    """;

    // ---- configuration ----------------------------------------------------------------------------

    [Fact]
    public void IsConfigured_is_false_without_an_api_key()
    {
        var client = NewClient(new StubHandler("{}"));

        Assert.False(client.IsConfigured);
    }

    [Fact]
    public void IsConfigured_is_true_once_the_shared_anthropic_key_is_set()
    {
        var client = NewClient(new StubHandler("{}"), ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        Assert.True(client.IsConfigured);
    }

    [Fact]
    public async Task Calling_an_unconfigured_client_fails_loudly_rather_than_hitting_the_network()
    {
        var handler = new StubHandler(ToolUseResponse);
        var client = NewClient(handler);

        await Assert.ThrowsAsync<ValidationException>(() => client.CompleteAsync(SampleRequest()));
        Assert.Null(handler.Request);
    }

    // ---- request shape ----------------------------------------------------------------------------

    [Fact]
    public async Task The_request_carries_the_required_headers_and_endpoint()
    {
        var handler = new StubHandler(ToolUseResponse);
        var client = NewClient(handler, ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        await client.CompleteAsync(SampleRequest());

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/v1/messages", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("sk-test", Assert.Single(handler.Request.Headers.GetValues("x-api-key")));
        Assert.Equal("2023-06-01", Assert.Single(handler.Request.Headers.GetValues("anthropic-version")));
    }

    [Fact]
    public async Task The_request_body_carries_the_model_caps_tools_and_transcript()
    {
        var handler = new StubHandler(ToolUseResponse);
        var client = NewClient(handler, ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        await client.CompleteAsync(SampleRequest());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var root = body.RootElement;

        Assert.Equal(AnthropicAssistantClient.DefaultModel, root.GetProperty("model").GetString());
        Assert.Equal(AnthropicAssistantClient.MaxTokens, root.GetProperty("max_tokens").GetInt32());
        // Effort is nested under output_config — it is not a top-level parameter.
        Assert.Equal(AnthropicAssistantClient.DefaultEffort,
            root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("system prompt", root.GetProperty("system").GetString());
        Assert.Equal(AssistantToolCatalog.Definitions.Count, root.GetProperty("tools").GetArrayLength());

        var messages = root.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        // The turn's content array is passed through as an array, not re-encoded as a string.
        Assert.Equal(JsonValueKind.Array, messages[0].GetProperty("content").ValueKind);
    }

    [Theory]
    [InlineData("temperature")]
    [InlineData("top_p")]
    [InlineData("top_k")]
    [InlineData("budget_tokens")]
    public async Task The_request_omits_parameters_this_model_family_rejects(string parameter)
    {
        // These are all 400s on the current models. Sending any of them would break every assistant call,
        // so their absence is worth pinning rather than rediscovering in production.
        var handler = new StubHandler(ToolUseResponse);
        var client = NewClient(handler, ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        await client.CompleteAsync(SampleRequest());

        Assert.DoesNotContain($"\"{parameter}\"", handler.RequestBody);
    }

    [Fact]
    public async Task Thinking_is_left_at_its_default_rather_than_disabled()
    {
        // Explicitly disabling thinking on this model can make it emit a tool call as plain text (which then
        // silently never runs) and leak internal tags — so the parameter is deliberately not sent at all.
        var handler = new StubHandler(ToolUseResponse);
        var client = NewClient(handler, ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        await client.CompleteAsync(SampleRequest());

        Assert.DoesNotContain("\"thinking\"", handler.RequestBody);
    }

    [Fact]
    public async Task The_model_and_effort_can_be_overridden_by_configuration()
    {
        var handler = new StubHandler(ToolUseResponse);
        var client = NewClient(handler,
            ("GoalFueled:Anthropic:ApiKey", "sk-test"),
            ("GoalFueled:Anthropic:AssistantModel", "claude-sonnet-5"),
            ("GoalFueled:Anthropic:AssistantEffort", "low"));

        await client.CompleteAsync(SampleRequest());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("claude-sonnet-5", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("low", body.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task An_unrecognized_effort_falls_back_to_the_default_instead_of_failing_every_call()
    {
        var handler = new StubHandler(ToolUseResponse);
        var client = NewClient(handler,
            ("GoalFueled:Anthropic:ApiKey", "sk-test"),
            ("GoalFueled:Anthropic:AssistantEffort", "ludicrous"));

        await client.CompleteAsync(SampleRequest());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(AnthropicAssistantClient.DefaultEffort,
            body.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
    }

    // ---- response parsing -------------------------------------------------------------------------

    [Fact]
    public async Task A_tool_use_response_is_parsed_into_text_and_tool_calls()
    {
        var client = NewClient(new StubHandler(ToolUseResponse), ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        var reply = await client.CompleteAsync(SampleRequest());

        Assert.Equal("tool_use", reply.StopReason);
        Assert.Equal("Let me check your library.", reply.Text);
        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal("toolu_1", call.Id);
        Assert.Equal(AssistantToolCatalog.SearchFuelSources, call.Name);
        Assert.Equal("apple", call.Input.GetProperty("query").GetString());
        // The raw content array survives for the echo back on the next turn.
        Assert.Equal(2, reply.Content.GetArrayLength());
    }

    [Fact]
    public async Task A_tool_call_the_catalog_does_not_define_is_ignored()
    {
        const string body = """
        {
          "stop_reason": "tool_use",
          "content": [
            { "type": "tool_use", "id": "t1", "name": "delete_everything", "input": {} },
            { "type": "tool_use", "id": "t2", "name": "log_burn", "input": { "name": "Run", "calories": 10 } }
          ]
        }
        """;
        var client = NewClient(new StubHandler(body), ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        var reply = await client.CompleteAsync(SampleRequest());

        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal(AssistantToolCatalog.LogBurn, call.Name);
    }

    [Fact]
    public async Task Unknown_content_block_types_are_skipped_but_kept_in_the_echoed_content()
    {
        // Thinking blocks (and anything else the provider adds later) must round-trip untouched even though
        // this app does not interpret them.
        const string body = """
        {
          "stop_reason": "end_turn",
          "content": [
            { "type": "thinking", "thinking": "", "signature": "abc123" },
            { "type": "text", "text": "All done." }
          ]
        }
        """;
        var client = NewClient(new StubHandler(body), ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        var reply = await client.CompleteAsync(SampleRequest());

        Assert.Equal("All done.", reply.Text);
        Assert.Empty(reply.ToolCalls);
        Assert.Equal(2, reply.Content.GetArrayLength());
        Assert.Contains("abc123", reply.Content.GetRawText());
    }

    [Fact]
    public async Task A_refusal_with_no_content_is_reported_rather_than_treated_as_malformed()
    {
        var client = NewClient(
            new StubHandler("""{ "stop_reason": "refusal" }"""), ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        var reply = await client.CompleteAsync(SampleRequest());

        Assert.Equal("refusal", reply.StopReason);
        Assert.Null(reply.Text);
        Assert.Empty(reply.ToolCalls);
    }

    [Fact]
    public async Task An_error_status_becomes_a_friendly_recoverable_failure()
    {
        var client = NewClient(
            new StubHandler("""{ "error": { "message": "overloaded" } }""", HttpStatusCode.ServiceUnavailable),
            ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => client.CompleteAsync(SampleRequest()));

        // The user gets a route forward, and the provider's raw error is not leaked to them.
        Assert.Contains("normal Fuel and Burn forms", ex.Message);
        Assert.DoesNotContain("overloaded", ex.Message);
    }

    [Fact]
    public async Task A_malformed_body_becomes_a_friendly_recoverable_failure()
    {
        var client = NewClient(new StubHandler("not json at all"), ("GoalFueled:Anthropic:ApiKey", "sk-test"));

        await Assert.ThrowsAsync<NotFoundException>(() => client.CompleteAsync(SampleRequest()));
    }
}
