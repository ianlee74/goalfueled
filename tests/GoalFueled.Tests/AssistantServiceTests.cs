using System.Text.Json;
using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Api.Services.Assistant;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Features;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Tests;

/// <summary>
/// Exercises the AI assistant end to end — the agentic loop, tool dispatch onto the real services, the
/// confirm-before-acting turn, the caps, and the feature gate — with the model faked, so nothing here
/// touches the network or needs an API key.
/// </summary>
public class AssistantServiceTests
{
    // ---- fakes ------------------------------------------------------------------------------------

    /// <summary>
    /// A scripted model. Replies are returned in order; once the script is down to its last entry that
    /// entry repeats, which is what lets a "always asks for another tool" script drive the loop cap.
    /// </summary>
    private sealed class FakeModelClient : IAssistantModelClient
    {
        private readonly Queue<AssistantModelReply> _replies;

        public FakeModelClient(params AssistantModelReply[] replies) => _replies = new Queue<AssistantModelReply>(replies);

        public bool IsConfigured { get; init; } = true;

        /// <summary>Every request the loop made, for asserting on what the model was actually told.</summary>
        public List<AssistantModelRequest> Requests { get; } = new();

        public int Calls => Requests.Count;

        public Task<AssistantModelReply> CompleteAsync(AssistantModelRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var reply = _replies.Count > 1 ? _replies.Dequeue() : _replies.Peek();
            return Task.FromResult(reply);
        }
    }

    /// <summary>A client that is present but unconfigured; calling it would be a bug, so it throws.</summary>
    private sealed class UnconfiguredModelClient : IAssistantModelClient
    {
        public bool IsConfigured => false;
        public Task<AssistantModelReply> CompleteAsync(AssistantModelRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("The loop must not call an unconfigured model client.");
    }

    // ---- reply builders ---------------------------------------------------------------------------

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, HttpResults.Json);

    /// <summary>Builds a model reply whose raw content array matches the blocks it reports, as the wire does.</summary>
    private static AssistantModelReply Reply(string? text, params (string Id, string Name, object Input)[] toolCalls)
    {
        var blocks = new List<object>();
        if (text is not null) blocks.Add(new { type = "text", text });
        foreach (var (id, name, input) in toolCalls)
            blocks.Add(new { type = "tool_use", id, name, input });

        var calls = toolCalls
            .Select(c => new AssistantToolCall(c.Id, c.Name, Json(c.Input)))
            .ToList();

        return new AssistantModelReply(
            toolCalls.Length > 0 ? "tool_use" : "end_turn", text, calls, Json(blocks));
    }

    private static AssistantChatInput Ask(string message) => new(message);

    // ---- fixture ----------------------------------------------------------------------------------

    private sealed record Fixture(
        AppUser User,
        AppUser Admin,
        AssistantService Assistant,
        FakeModelClient Model,
        IngredientService Ingredients,
        RecipeService Recipes,
        MealService Meals,
        ConsumptionService Consumption,
        BurnService Burn);

    private static async Task<Fixture> SetupAsync(GoalFueledDbContext db, IAssistantModelClient client)
    {
        var admin = (await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "admin", "admin@dev.local", "Admin"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest()))!;
        var user = (await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest()))!;

        // The assistant is a Pro-tier feature; grant it individually so the tests exercise the gated path
        // without depending on the tiering decision staying put.
        FeatureAccess.SetOverride(user, FeatureKeys.AiVoiceLogging, FeatureAccessMode.Granted);
        FeatureAccess.SetOverride(admin, FeatureKeys.AiVoiceLogging, FeatureAccessMode.Granted);

        var ingredients = new IngredientService(new IngredientRepository(db));
        var recipes = new RecipeService(new RecipeRepository(db), new IngredientRepository(db));
        var meals = new MealService(new MealRepository(db), new RecipeRepository(db), new IngredientRepository(db));
        var plans = new MealPlanService(
            new MealPlanRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db));
        var consumption = new ConsumptionService(
            new ConsumptionEntryRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db),
            new WeightEntryRepository(db), plans, TestHelpers.NewAwards(db));
        var burn = new BurnService(new BurnActivityRepository(db), new WeightEntryRepository(db), TestHelpers.NewAwards(db));
        // The nutrition lookup's own HTTP client is faked out to "source unavailable" (null response), which
        // is exactly what an offline/rate-limited environment looks like.
        var nutrition = new NutritionLookupService(new NullFdcClient(), new IngredientRepository(db));

        var dispatcher = new AssistantToolDispatcher(ingredients, recipes, meals, consumption, burn, nutrition);
        var assistant = new AssistantService(client, dispatcher);

        return new Fixture(user, admin, assistant,
            client as FakeModelClient ?? new FakeModelClient(),
            ingredients, recipes, meals, consumption, burn);
    }

    /// <summary>USDA stand-in that always reports "unreachable", so no test can reach the network.</summary>
    private sealed class NullFdcClient : IFdcNutritionClient
    {
        public Task<FdcSearchResponse?> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult<FdcSearchResponse?>(null);
    }

    /// <summary>Creates a gram-based ingredient with the given calories per gram, owned by the user.</summary>
    private static Task<IngredientDto> NewIngredientAsync(
        IngredientService ingredients, AppUser user, string name, double caloriesPerGram = 1) =>
        ingredients.CreateAsync(user, new IngredientInput(
            name, null, null, UnitOfMeasure.Gram, 1,
            new List<NutrientAmount> { new("calories", caloriesPerGram) }), default);

    // ---- feature gate -----------------------------------------------------------------------------

    [Fact]
    public async Task Chat_requires_the_ai_voice_logging_feature()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply("Done."));
        var fx = await SetupAsync(db, model);

        // Take the grant away: a Free-tier account without it must be refused.
        FeatureAccess.SetOverride(fx.User, FeatureKeys.AiVoiceLogging, FeatureAccessMode.Inherit);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => fx.Assistant.ChatAsync(fx.User, Ask("I ate an apple")));

        Assert.Contains("AI voice logging", ex.Message);
        // The gate must come before any billable call.
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Chat_is_refused_even_for_an_admin_without_the_feature()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply("Done."));
        var fx = await SetupAsync(db, model);
        FeatureAccess.SetOverride(fx.Admin, FeatureKeys.AiVoiceLogging, FeatureAccessMode.Inherit);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => fx.Assistant.ChatAsync(fx.Admin, Ask("I ate an apple")));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void Ai_voice_logging_is_a_beta_feature_in_the_catalog()
    {
        var feature = FeatureCatalog.Find(FeatureKeys.AiVoiceLogging);

        Assert.NotNull(feature);
        Assert.Equal(FeatureStatus.Beta, feature!.Status);
        // Beta features are handed out by tier (unlike Alpha) and are enableable (unlike Planned).
        Assert.True(feature.IsEnableable);
        Assert.True(feature.IsIncludedBySubscription);
    }

    // ---- unconfigured key -------------------------------------------------------------------------

    [Fact]
    public async Task Chat_without_an_api_key_degrades_to_an_honest_message()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new UnconfiguredModelClient());

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("I ate an apple"));

        Assert.Equal(AssistantChatStatus.Unavailable, result.Status);
        Assert.Equal(AssistantService.UnavailableMessage, result.Reply);
        Assert.Empty(result.Actions);
        Assert.Empty(result.Conversation);
        Assert.Null(result.PendingToolUseId);
    }

    [Fact]
    public async Task Chat_still_validates_the_message_before_checking_configuration()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new UnconfiguredModelClient());

        await Assert.ThrowsAsync<ValidationException>(() => fx.Assistant.ChatAsync(fx.User, new AssistantChatInput("   ")));
        await Assert.ThrowsAsync<ValidationException>(
            () => fx.Assistant.ChatAsync(fx.User, new AssistantChatInput(new string('x', 2_001))));
    }

    // ---- plain conversation -----------------------------------------------------------------------

    [Fact]
    public async Task Chat_with_no_tool_calls_returns_the_model_text()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("I logged nothing — tell me what you ate.")));

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("hello"));

        Assert.Equal(AssistantChatStatus.Completed, result.Status);
        Assert.Equal("I logged nothing — tell me what you ate.", result.Reply);
        // user turn + assistant turn.
        Assert.Equal(2, result.Conversation.Count);
        Assert.Equal("user", result.Conversation[0].Role);
        Assert.Equal("assistant", result.Conversation[1].Role);
    }

    [Fact]
    public async Task Chat_sends_the_system_prompt_and_the_tool_catalog()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply("Done."));
        var fx = await SetupAsync(db, model);

        await fx.Assistant.ChatAsync(fx.User, Ask("hello"));

        var request = Assert.Single(model.Requests);
        Assert.Contains("GoalFueled logging assistant", request.SystemPrompt);
        Assert.Contains(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), request.SystemPrompt);
        Assert.Same(AssistantToolCatalog.Definitions, request.Tools);
        Assert.Equal(10, AssistantToolCatalog.Definitions.Count);
    }

    [Fact]
    public async Task Chat_treats_a_refusal_as_a_completed_turn_rather_than_an_empty_reply()
    {
        using var db = TestHelpers.NewContext();
        var refusal = new AssistantModelReply(
            "refusal", null, Array.Empty<AssistantToolCall>(), Json(Array.Empty<object>()));
        var fx = await SetupAsync(db, new FakeModelClient(refusal));

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("something the classifier dislikes"));

        Assert.Equal(AssistantChatStatus.Completed, result.Status);
        Assert.Contains("can't help with that", result.Reply);
        Assert.Empty(result.Actions);
    }

    // ---- tool dispatch ----------------------------------------------------------------------------

    [Fact]
    public async Task Log_burn_tool_records_a_real_burn_activity()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.LogBurn, new { name = "Morning run", calories = 420 })),
            Reply("Logged your run.")));

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("I went for a run this morning"));

        Assert.Equal(AssistantChatStatus.Completed, result.Status);
        var day = await fx.Burn.GetDayAsync(fx.User, null, default);
        var activity = Assert.Single(day.Activities);
        Assert.Equal("Morning run", activity.Name);
        Assert.Equal(420, activity.CaloriesBurned);

        // The UI is shown what actually happened, not the model's prose.
        var action = Assert.Single(result.Actions);
        Assert.Equal("burn", action.Kind);
        Assert.Equal(activity.Id, action.Id);
        Assert.Contains("Morning run", action.Summary);
    }

    [Fact]
    public async Task Log_fuel_tool_logs_an_existing_fuel_source_with_computed_nutrition()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("Logged.")));

        // A fuel source worth 250 kcal per serving, already in the user's library.
        var ingredient = await NewIngredientAsync(fx.Ingredients, fx.User, "kcal-base");
        var recipe = await fx.Recipes.CreateAsync(fx.User, new RecipeInput(
            "Burger patty", null, null,
            new List<RecipeItemInput> { new(ingredient.Id, 250, UnitOfMeasure.Gram) }, 1), default);
        var meal = await fx.Meals.CreateAsync(fx.User, new MealInput(
            "Quarter Pounder", new List<MealPortionInput> { new(recipe.Id, 1) }), default);

        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.LogFuel, new { fuelSourceId = meal.Id, servings = 2 })),
            Reply("Logged two Quarter Pounders."));
        var fx2 = fx with { Assistant = new AssistantService(model, new AssistantToolDispatcher(
            fx.Ingredients, fx.Recipes, fx.Meals, fx.Consumption, fx.Burn,
            new NutritionLookupService(new NullFdcClient(), new IngredientRepository(db)))) };

        var result = await fx2.Assistant.ChatAsync(fx.User, Ask("I ate two quarter pounders"));

        Assert.Equal(AssistantChatStatus.Completed, result.Status);
        var day = await fx.Consumption.GetDayAsync(fx.User, null, default);
        var entry = Assert.Single(day.Entries);
        Assert.Equal(ConsumptionSource.Meal, entry.Source);
        Assert.Equal(meal.Id, entry.SourceId);
        Assert.Equal(2, entry.Servings);
        // Nutrition is computed live from the catalog, not taken from the model.
        Assert.Equal(500, entry.Nutrition.Single(n => n.NutrientId == "calories").Value, 3);
    }

    [Fact]
    public async Task Log_fuel_tool_supports_a_free_form_entry_when_nothing_exists()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.LogFuel, new
            {
                name = "Diner special",
                servings = 1,
                nutrition = new[] { new { nutrientId = "calories", value = 730 } },
            })),
            Reply("Logged it as a one-off.")));

        await fx.Assistant.ChatAsync(fx.User, Ask("I ate the diner special"));

        var day = await fx.Consumption.GetDayAsync(fx.User, null, default);
        var entry = Assert.Single(day.Entries);
        Assert.Equal(ConsumptionSource.FreeForm, entry.Source);
        Assert.Equal("Diner special", entry.Name);
        Assert.Equal(730, entry.Nutrition.Single(n => n.NutrientId == "calories").Value, 3);
    }

    [Fact]
    public async Task Create_tools_build_an_ingredient_recipe_and_fuel_source_owned_privately_by_the_user()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("placeholder")));

        // Step 1: create the ingredient.
        var m1 = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.CreateIngredient, new
            {
                name = "French fries",
                unit = "Gram",
                servingSize = 100,
                nutrition = new[] { new { nutrientId = "calories", value = 312 } },
            })),
            Reply("Created."));
        var a1 = new AssistantService(m1, Dispatcher(db, fx));
        var r1 = await a1.ChatAsync(fx.User, Ask("make an ingredient"));

        var ingredientId = Assert.Single(r1.Actions, a => a.Kind == "ingredient").Id!;
        var ingredient = await fx.Ingredients.GetAsync(fx.User, ingredientId, default);
        Assert.Equal(OwnershipScope.User, ingredient.Scope);
        Assert.Equal(fx.User.Id, ingredient.OwnerUserId);
        Assert.Equal(IngredientVisibility.Private, ingredient.Visibility);

        // Step 2: a recipe over it.
        var m2 = new FakeModelClient(
            Reply(null, ("t2", AssistantToolCatalog.CreateRecipe, new
            {
                name = "Medium fries",
                servings = 1,
                items = new[] { new { ingredientId, quantity = 117, unit = "Gram" } },
            })),
            Reply("Created."));
        var r2 = await new AssistantService(m2, Dispatcher(db, fx)).ChatAsync(fx.User, Ask("make a recipe"));
        var recipeId = Assert.Single(r2.Actions, a => a.Kind == "recipe").Id!;
        var recipe = await fx.Recipes.GetAsync(fx.User, recipeId, default);
        Assert.Equal(OwnershipScope.User, recipe.Scope);
        Assert.Equal(RecipeVisibility.Private, recipe.Visibility);

        // Step 3: a fuel source over the recipe.
        var m3 = new FakeModelClient(
            Reply(null, ("t3", AssistantToolCatalog.CreateFuelSource, new
            {
                name = "Quarter Pounder with fries",
                servings = 1,
                portions = new[] { new { recipeId, servings = 1.0 } },
            })),
            Reply("Created."));
        var r3 = await new AssistantService(m3, Dispatcher(db, fx)).ChatAsync(fx.User, Ask("make a fuel source"));
        var mealId = Assert.Single(r3.Actions, a => a.Kind == "fuel-source").Id!;
        var meal = await fx.Meals.GetAsync(fx.User, mealId, default);
        Assert.Equal(OwnershipScope.User, meal.Scope);
        Assert.Equal(fx.User.Id, meal.OwnerUserId);
    }

    [Fact]
    public async Task Created_content_stays_private_even_when_the_caller_is_an_admin()
    {
        // The dispatcher never sets AppLevel, so an admin's assistant-created items are still their own.
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("placeholder")));

        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.CreateIngredient, new
            {
                name = "Admin snack",
                unit = "Gram",
                servingSize = 50,
                nutrition = new[] { new { nutrientId = "calories", value = 100 } },
            })),
            Reply("Created."));

        var result = await new AssistantService(model, Dispatcher(db, fx)).ChatAsync(fx.Admin, Ask("make it"));

        var id = Assert.Single(result.Actions).Id!;
        var ingredient = await fx.Ingredients.GetAsync(fx.Admin, id, default);
        Assert.Equal(OwnershipScope.User, ingredient.Scope);
        Assert.Equal(fx.Admin.Id, ingredient.OwnerUserId);
    }

    [Fact]
    public async Task Search_tools_only_return_items_the_caller_can_see()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("placeholder")));

        // An ingredient owned by someone else must not appear in the caller's search results.
        var stranger = (await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "stranger", "s@dev.local", "Stranger"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest()))!;
        await NewIngredientAsync(fx.Ingredients, stranger, "Secret sauce");
        await NewIngredientAsync(fx.Ingredients, fx.User, "My sauce");

        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.SearchIngredients, new { query = "sauce" })),
            Reply("Found them."));
        await new AssistantService(model, Dispatcher(db, fx)).ChatAsync(fx.User, Ask("find sauce"));

        // The tool result is the second-to-last turn's content (the user message carrying tool results).
        var toolResults = model.Requests[^1].Messages[^1].Content.GetRawText();
        Assert.Contains("My sauce", toolResults);
        Assert.DoesNotContain("Secret sauce", toolResults);
    }

    [Fact]
    public async Task A_failing_tool_comes_back_as_an_error_result_so_the_model_can_recover()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("placeholder")));

        // Logging a fuel source that does not exist is a ValidationException inside ConsumptionService.
        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.LogFuel, new { fuelSourceId = "does-not-exist", servings = 1 })),
            Reply("I couldn't find that one."));

        var result = await new AssistantService(model, Dispatcher(db, fx)).ChatAsync(fx.User, Ask("log it"));

        // The turn completes normally; the model was handed the failure rather than the request 500ing.
        Assert.Equal(AssistantChatStatus.Completed, result.Status);
        Assert.Empty(result.Actions);
        var toolResults = model.Requests[^1].Messages[^1].Content.GetRawText();
        Assert.Contains("is_error", toolResults);
        Assert.Contains("not found", toolResults, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_nutrient_id_is_dropped_rather_than_stored()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("placeholder")));

        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.CreateIngredient, new
            {
                name = "Odd item",
                unit = "Gram",
                servingSize = 10,
                nutrition = new[]
                {
                    new { nutrientId = "calories", value = 42.0 },
                    new { nutrientId = "unobtanium", value = 99.0 },
                },
            })),
            Reply("Created."));

        var result = await new AssistantService(model, Dispatcher(db, fx)).ChatAsync(fx.User, Ask("make it"));

        var ingredient = await fx.Ingredients.GetAsync(fx.User, Assert.Single(result.Actions).Id!, default);
        Assert.Equal(42, ingredient.NutritionPerServing.Single().Value);
        Assert.DoesNotContain(ingredient.NutritionPerServing, n => n.NutrientId == "unobtanium");
    }

    [Fact]
    public async Task Nutrition_lookup_reports_not_found_without_failing_the_turn()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("placeholder")));

        // The USDA client is stubbed to "unreachable" and the catalog is empty, so the lookup finds nothing.
        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.LookupNutrition, new { query = "quarter pounder" })),
            Reply("I'll estimate instead."));

        var result = await new AssistantService(model, Dispatcher(db, fx)).ChatAsync(fx.User, Ask("look it up"));

        Assert.Equal(AssistantChatStatus.Completed, result.Status);
        var toolResults = model.Requests[^1].Messages[^1].Content.GetRawText();
        // Reported as data ("found: false"), not as a tool error — the model should carry on.
        Assert.Contains("\\u0022found\\u0022:false", toolResults);
        Assert.Contains("\"is_error\":false", toolResults);
    }

    // ---- the confirmation turn --------------------------------------------------------------------

    [Fact]
    public async Task Asking_to_confirm_pauses_the_loop_and_returns_the_question()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply(null, ("ask-1", AssistantToolCatalog.AskUserToConfirm, new
        {
            question = "I don't have a Quarter Pounder. Shall I create one?",
            details = "Beef patty 200 g + fries 117 g, about 740 kcal.",
        })));
        var fx = await SetupAsync(db, model);

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("I ate a quarter pounder with fries"));

        Assert.Equal(AssistantChatStatus.NeedsConfirmation, result.Status);
        Assert.Equal("ask-1", result.PendingToolUseId);
        Assert.Contains("Shall I create one?", result.Reply);
        Assert.Contains("740 kcal", result.Reply); // details are appended so the answer is informed.
        Assert.Empty(result.Actions);              // nothing was created before asking.
        // Only one model call was made — the loop stopped rather than pressing on.
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task Answering_a_confirmation_resumes_the_loop_and_completes_the_work()
    {
        using var db = TestHelpers.NewContext();

        // Turn 1: the model asks.
        var m1 = new FakeModelClient(Reply(null, ("ask-1", AssistantToolCatalog.AskUserToConfirm,
            new { question = "Shall I create it?" })));
        var fx = await SetupAsync(db, m1);
        var pending = await fx.Assistant.ChatAsync(fx.User, Ask("I ate a quarter pounder"));
        Assert.Equal(AssistantChatStatus.NeedsConfirmation, pending.Status);

        // Turn 2: the user says yes; the model then logs.
        var m2 = new FakeModelClient(
            Reply(null, ("t2", AssistantToolCatalog.LogBurn, new { name = "Confirmed activity", calories = 100 })),
            Reply("All set."));
        var resumed = await new AssistantService(m2, Dispatcher(db, fx)).ChatAsync(
            fx.User,
            new AssistantChatInput("yes", pending.Conversation, pending.PendingToolUseId));

        Assert.Equal(AssistantChatStatus.Completed, resumed.Status);
        Assert.Equal("All set.", resumed.Reply);
        Assert.Single(resumed.Actions);

        // The user's answer went back as the pending call's tool_result, in a user turn following the ask.
        var sent = m2.Requests[0].Messages;
        var answerTurn = sent[^1];
        Assert.Equal("user", answerTurn.Role);
        var raw = answerTurn.Content.GetRawText();
        Assert.Contains("\"tool_use_id\":\"ask-1\"", raw);
        Assert.Contains("yes", raw);
    }

    [Fact]
    public async Task A_confirmation_alongside_other_tool_calls_runs_the_earlier_ones_and_defers_the_rest()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply(null,
            ("t1", AssistantToolCatalog.SearchFuelSources, new { query = "burger" }),
            ("ask-1", AssistantToolCatalog.AskUserToConfirm, new { question = "Create it?" }),
            ("t3", AssistantToolCatalog.LogBurn, new { name = "Should not run", calories = 50 })));
        var fx = await SetupAsync(db, model);

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("I ate a burger"));

        Assert.Equal(AssistantChatStatus.NeedsConfirmation, result.Status);
        Assert.Equal("ask-1", result.PendingToolUseId);

        // The call after the question was NOT executed — nothing was logged.
        var day = await fx.Burn.GetDayAsync(fx.User, null, default);
        Assert.Empty(day.Activities);

        // The partial user turn carries results for the search and the deferred call, but not the question.
        var partial = result.Conversation[^1];
        Assert.Equal("user", partial.Role);
        var raw = partial.Content.GetRawText();
        Assert.Contains("\"tool_use_id\":\"t1\"", raw);
        Assert.Contains("\"tool_use_id\":\"t3\"", raw);
        Assert.DoesNotContain("\"tool_use_id\":\"ask-1\"", raw);
        Assert.Contains("Not executed", raw);
    }

    [Fact]
    public async Task Answering_a_confirmation_appends_to_the_partial_tool_result_turn()
    {
        using var db = TestHelpers.NewContext();
        var m1 = new FakeModelClient(Reply(null,
            ("t1", AssistantToolCatalog.SearchRecipes, new { query = "fries" }),
            ("ask-1", AssistantToolCatalog.AskUserToConfirm, new { question = "Create it?" })));
        var fx = await SetupAsync(db, m1);
        var pending = await fx.Assistant.ChatAsync(fx.User, Ask("I ate fries"));

        var m2 = new FakeModelClient(Reply("Done."));
        await new AssistantService(m2, Dispatcher(db, fx)).ChatAsync(
            fx.User, new AssistantChatInput("yes please", pending.Conversation, pending.PendingToolUseId));

        // Both results must live in ONE user message: the provider requires every tool_use in a turn to be
        // answered by results in a single following user message.
        var sent = m2.Requests[0].Messages;
        Assert.Equal("user", sent[^1].Role);
        var raw = sent[^1].Content.GetRawText();
        Assert.Contains("\"tool_use_id\":\"t1\"", raw);
        Assert.Contains("\"tool_use_id\":\"ask-1\"", raw);
        Assert.Equal(2, sent[^1].Content.GetArrayLength());
    }

    [Fact]
    public async Task Answering_with_no_conversation_is_rejected()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("Done.")));

        await Assert.ThrowsAsync<ValidationException>(
            () => fx.Assistant.ChatAsync(fx.User, new AssistantChatInput("yes", null, "ask-1")));
    }

    // ---- caps -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_loop_stops_after_the_iteration_cap()
    {
        using var db = TestHelpers.NewContext();
        // A model that asks for the same (harmless) search forever.
        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.SearchRecipes, new { query = "loop" })));
        var fx = await SetupAsync(db, model);

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("go in circles"));

        Assert.Equal(AssistantChatStatus.Completed, result.Status);
        Assert.Equal(AssistantService.MaxIterations, model.Calls);
        Assert.Contains("couldn't work that out", result.Reply);
    }

    [Fact]
    public async Task The_loop_cap_still_reports_what_it_managed_to_do()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(
            Reply(null, ("t1", AssistantToolCatalog.LogBurn, new { name = "Repeated", calories = 10 })));
        var fx = await SetupAsync(db, model);

        var result = await fx.Assistant.ChatAsync(fx.User, Ask("log repeatedly"));

        Assert.Equal(AssistantService.MaxIterations, model.Calls);
        Assert.Equal(AssistantService.MaxIterations, result.Actions.Count);
        Assert.Contains("stopped there", result.Reply);
    }

    [Fact]
    public async Task An_oversized_conversation_is_rejected_before_any_model_call()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply("Done."));
        var fx = await SetupAsync(db, model);

        var turns = Enumerable.Range(0, InputLimits.AssistantConversationTurnsMax + 1)
            .Select(_ => new AssistantTurnDto("user", Json(new[] { new { type = "text", text = "x" } })))
            .ToList();

        await Assert.ThrowsAsync<ValidationException>(
            () => fx.Assistant.ChatAsync(fx.User, new AssistantChatInput("hi", turns)));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task A_conversation_that_is_too_large_by_characters_is_rejected()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply("Done."));
        var fx = await SetupAsync(db, model);

        // Few turns, but each enormous — the turn count alone would not have caught this.
        var big = new string('x', 60_000);
        var turns = Enumerable.Range(0, 4)
            .Select(_ => new AssistantTurnDto("user", Json(new[] { new { type = "text", text = big } })))
            .ToList();

        await Assert.ThrowsAsync<ValidationException>(
            () => fx.Assistant.ChatAsync(fx.User, new AssistantChatInput("hi", turns)));
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("tool")]
    [InlineData("")]
    public async Task A_conversation_turn_with_an_unexpected_role_is_rejected(string role)
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("Done.")));

        var turns = new List<AssistantTurnDto>
        {
            new(role, Json(new[] { new { type = "text", text = "x" } })),
        };

        await Assert.ThrowsAsync<ValidationException>(
            () => fx.Assistant.ChatAsync(fx.User, new AssistantChatInput("hi", turns)));
    }

    [Fact]
    public async Task A_conversation_turn_whose_content_is_not_an_array_is_rejected()
    {
        using var db = TestHelpers.NewContext();
        var fx = await SetupAsync(db, new FakeModelClient(Reply("Done.")));

        var turns = new List<AssistantTurnDto> { new("user", Json(new { type = "text" })) };

        await Assert.ThrowsAsync<ValidationException>(
            () => fx.Assistant.ChatAsync(fx.User, new AssistantChatInput("hi", turns)));
    }

    [Fact]
    public async Task A_prior_conversation_is_replayed_to_the_model_ahead_of_the_new_message()
    {
        using var db = TestHelpers.NewContext();
        var model = new FakeModelClient(Reply("Done."));
        var fx = await SetupAsync(db, model);

        var prior = new List<AssistantTurnDto>
        {
            new("user", Json(new[] { new { type = "text", text = "earlier question" } })),
            new("assistant", Json(new[] { new { type = "text", text = "earlier answer" } })),
        };

        var result = await fx.Assistant.ChatAsync(fx.User, new AssistantChatInput("follow-up", prior));

        var sent = Assert.Single(model.Requests).Messages;
        Assert.Equal(3, sent.Count);
        Assert.Contains("earlier question", sent[0].Content.GetRawText());
        Assert.Contains("follow-up", sent[2].Content.GetRawText());
        // The returned conversation grows by the new user turn plus the assistant's reply.
        Assert.Equal(4, result.Conversation.Count);
    }

    /// <summary>A dispatcher over the fixture's services, for tests that swap in a different scripted model.</summary>
    private static AssistantToolDispatcher Dispatcher(GoalFueledDbContext db, Fixture fx) =>
        new(fx.Ingredients, fx.Recipes, fx.Meals, fx.Consumption, fx.Burn,
            new NutritionLookupService(new NullFdcClient(), new IngredientRepository(db)));
}
