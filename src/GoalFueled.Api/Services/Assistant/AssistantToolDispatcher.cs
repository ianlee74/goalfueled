using System.Text.Json;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services.Assistant;

/// <summary>
/// Executes one model-requested tool call against the real GoalFueled services, as the calling user.
/// </summary>
/// <remarks>
/// <para>This is the security boundary of the whole feature: the model produces <em>intent</em>, this class
/// turns intent into ordinary service calls. Nothing here writes to a repository directly, so every
/// existing rule — ownership, visibility, unit compatibility, input limits, future-date checks, gamification
/// side effects — applies unchanged. Anything created is <see cref="OwnershipScope.User"/> and private:
/// the create paths never set <c>AppLevel</c>, so even an admin caller gets a private item.</para>
/// <para>A failed tool call is returned to the model as an error result rather than aborting the turn, so it
/// can correct itself (pick a different ingredient, fix a unit) the way a person would.</para>
/// </remarks>
public sealed class AssistantToolDispatcher
{
    private readonly IngredientService _ingredients;
    private readonly RecipeService _recipes;
    private readonly MealService _meals;
    private readonly ConsumptionService _consumption;
    private readonly BurnService _burn;
    private readonly NutritionLookupService _nutrition;

    public AssistantToolDispatcher(
        IngredientService ingredients,
        RecipeService recipes,
        MealService meals,
        ConsumptionService consumption,
        BurnService burn,
        NutritionLookupService nutrition)
    {
        _ingredients = ingredients;
        _recipes = recipes;
        _meals = meals;
        _consumption = consumption;
        _burn = burn;
        _nutrition = nutrition;
    }

    /// <summary>The outcome of one tool call: what the model is told, and what the user is shown.</summary>
    public sealed record Outcome(
        /// <summary>JSON payload handed back to the model as the tool result.</summary>
        string ResultJson,
        /// <summary>True when the call failed; the model sees it flagged and can retry differently.</summary>
        bool IsError,
        /// <summary>Set only for successful data-changing calls, for the UI's "what actually happened" list.</summary>
        AssistantActionDto? Action = null);

    /// <summary>Runs one tool call. Never throws for ordinary failures — those come back as error results.</summary>
    public async Task<Outcome> ExecuteAsync(AppUser user, AssistantToolCall call, CancellationToken ct)
    {
        try
        {
            return call.Name switch
            {
                AssistantToolCatalog.SearchIngredients => await SearchIngredientsAsync(user, call.Input, ct),
                AssistantToolCatalog.SearchRecipes => await SearchRecipesAsync(user, call.Input, ct),
                AssistantToolCatalog.SearchFuelSources => await SearchFuelSourcesAsync(user, call.Input, ct),
                AssistantToolCatalog.LookupNutrition => await LookupNutritionAsync(user, call.Input, ct),
                AssistantToolCatalog.CreateIngredient => await CreateIngredientAsync(user, call.Input, ct),
                AssistantToolCatalog.CreateRecipe => await CreateRecipeAsync(user, call.Input, ct),
                AssistantToolCatalog.CreateFuelSource => await CreateFuelSourceAsync(user, call.Input, ct),
                AssistantToolCatalog.LogFuel => await LogFuelAsync(user, call.Input, ct),
                AssistantToolCatalog.LogBurn => await LogBurnAsync(user, call.Input, ct),
                _ => Error($"Unknown tool '{call.Name}'."),
            };
        }
        catch (ApiException ex)
        {
            // Validation/not-found/forbidden are normal conversational outcomes — let the model react.
            return Error(ex.Message);
        }
    }

    // ---- read tools -------------------------------------------------------------------------------

    private async Task<Outcome> SearchIngredientsAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var query = RequiredString(input, "query");
        var hits = await _ingredients.ListAsync(user, query, all: false, ct);
        return Ok(new
        {
            matches = hits.Take(AssistantToolCatalog.SearchResultLimit).Select(i => new
            {
                id = i.Id,
                name = i.Name,
                unit = i.Unit.ToString(),
                servingSize = i.ServingSize,
                caloriesPerServing = Calories(i.NutritionPerServing),
            }),
        });
    }

    private async Task<Outcome> SearchRecipesAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var query = RequiredString(input, "query");
        var hits = await _recipes.ListAsync(user, query, all: false, ct);
        return Ok(new
        {
            matches = hits.Take(AssistantToolCatalog.SearchResultLimit).Select(r => new
            {
                id = r.Id,
                name = r.Name,
                servings = r.Servings,
                fuelType = r.FuelType.ToString(),
                caloriesPerServing = Calories(r.NutritionPerServing),
            }),
        });
    }

    private async Task<Outcome> SearchFuelSourcesAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var query = RequiredString(input, "query");
        var hits = await _meals.ListAsync(user, query, all: false, ct);
        return Ok(new
        {
            matches = hits.Take(AssistantToolCatalog.SearchResultLimit).Select(m => new
            {
                id = m.Id,
                name = m.Name,
                servings = m.Servings,
                fuelType = m.FuelType.ToString(),
                caloriesPerServing = Calories(m.NutritionPerServing),
            }),
        });
    }

    private async Task<Outcome> LookupNutritionAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var query = RequiredString(input, "query");
        try
        {
            var result = await _nutrition.LookupAsync(user.Id, query, ct);
            return Ok(new
            {
                found = true,
                matchedName = result.MatchedName,
                source = result.Source,
                basisGrams = result.BasisGrams,
                nutritionPer100g = result.NutritionPer100g.Select(n => new { nutrientId = n.NutrientId, value = n.Value }),
            });
        }
        catch (NotFoundException ex)
        {
            // "No match" is information, not a failure — a non-error result keeps the model on task.
            return Ok(new { found = false, reason = ex.Message });
        }
    }

    // ---- write tools ------------------------------------------------------------------------------

    private async Task<Outcome> CreateIngredientAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var created = await _ingredients.CreateAsync(user, new IngredientInput(
            Name: RequiredString(input, "name"),
            Description: null,
            ImageUrl: null,
            Unit: RequiredEnum<UnitOfMeasure>(input, "unit"),
            ServingSize: RequiredNumber(input, "servingSize"),
            NutritionPerServing: ReadNutrition(input, "nutrition")), ct);

        return Ok(new { id = created.Id, name = created.Name },
            new AssistantActionDto("ingredient", $"Created ingredient \"{created.Name}\"", created.Id));
    }

    private async Task<Outcome> CreateRecipeAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var items = new List<RecipeItemInput>();
        foreach (var item in ReadArray(input, "items"))
        {
            items.Add(new RecipeItemInput(
                RequiredString(item, "ingredientId"),
                RequiredNumber(item, "quantity"),
                RequiredEnum<UnitOfMeasure>(item, "unit")));
        }

        var created = await _recipes.CreateAsync(user, new RecipeInput(
            Name: RequiredString(input, "name"),
            Description: null,
            ImageUrl: null,
            Items: items,
            Servings: RequiredNumber(input, "servings"),
            AppLevel: false, // assistant-created content is always private to the caller.
            FuelType: OptionalEnum<FuelType>(input, "fuelType") ?? FuelType.Food), ct);

        return Ok(new { id = created.Id, name = created.Name, servings = created.Servings },
            new AssistantActionDto("recipe", $"Created recipe \"{created.Name}\"", created.Id));
    }

    private async Task<Outcome> CreateFuelSourceAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var portions = new List<MealPortionInput>();
        foreach (var portion in ReadArray(input, "portions"))
            portions.Add(new MealPortionInput(RequiredString(portion, "recipeId"), RequiredNumber(portion, "servings")));

        var created = await _meals.CreateAsync(user, new MealInput(
            Name: RequiredString(input, "name"),
            Portions: portions,
            AppLevel: false, // assistant-created content is always private to the caller.
            Servings: OptionalNumber(input, "servings") ?? 1,
            FuelType: OptionalEnum<FuelType>(input, "fuelType") ?? FuelType.Food), ct);

        return Ok(new { id = created.Id, name = created.Name, servings = created.Servings },
            new AssistantActionDto("fuel-source", $"Created fuel source \"{created.Name}\"", created.Id));
    }

    private async Task<Outcome> LogFuelAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var fuelSourceId = OptionalString(input, "fuelSourceId");
        var name = OptionalString(input, "name");
        var servings = OptionalNumber(input, "servings") ?? 1;
        var date = OptionalDate(input, "date");

        var entry = fuelSourceId is not null
            ? await _consumption.LogAsync(user, new ConsumptionEntryInput(
                ConsumptionSource.Meal, SourceId: fuelSourceId, Name: name, Servings: servings, Date: date), ct)
            : await _consumption.LogAsync(user, new ConsumptionEntryInput(
                ConsumptionSource.FreeForm, Name: name, Servings: servings,
                Nutrition: ReadNutrition(input, "nutrition"), Date: date), ct);

        var servingText = entry.Servings == 1 ? "1 serving" : $"{entry.Servings:0.##} servings";
        return Ok(new
        {
            id = entry.Id,
            name = entry.Name,
            date = entry.Date.ToString("yyyy-MM-dd"),
            servings = entry.Servings,
            calories = Calories(entry.Nutrition),
        }, new AssistantActionDto("fuel", $"Logged {servingText} of {entry.Name}", entry.Id));
    }

    private async Task<Outcome> LogBurnAsync(AppUser user, JsonElement input, CancellationToken ct)
    {
        var activity = await _burn.LogAsync(user, new BurnActivityInput(
            Name: RequiredString(input, "name"),
            CaloriesBurned: RequiredInt(input, "calories"),
            Date: OptionalDate(input, "date"),
            Time: OptionalTime(input, "time")), ct);

        return Ok(new
        {
            id = activity.Id,
            name = activity.Name,
            date = activity.Date.ToString("yyyy-MM-dd"),
            calories = activity.CaloriesBurned,
        }, new AssistantActionDto("burn", $"Logged {activity.Name} ({activity.CaloriesBurned} kcal burned)", activity.Id));
    }

    // ---- result helpers ---------------------------------------------------------------------------

    private static Outcome Ok(object payload, AssistantActionDto? action = null) =>
        new(JsonSerializer.Serialize(payload, HttpResults.Json), IsError: false, action);

    private static Outcome Error(string message) =>
        new(JsonSerializer.Serialize(new { error = message }, HttpResults.Json), IsError: true);

    private static double? Calories(IReadOnlyList<NutrientAmount> amounts) =>
        amounts.FirstOrDefault(a => a.NutrientId == "calories")?.Value;

    // ---- input parsing ----------------------------------------------------------------------------
    // Tool inputs are model output: always parsed, never trusted. Every failure becomes a ValidationException,
    // which ExecuteAsync turns into an error tool result the model can react to.

    private static JsonElement? Property(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) &&
        value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static string RequiredString(JsonElement input, string name) =>
        OptionalString(input, name) ?? throw new ValidationException($"'{name}' is required.");

    private static string? OptionalString(JsonElement input, string name)
    {
        if (Property(input, name) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.String) throw new ValidationException($"'{name}' must be a string.");
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static double RequiredNumber(JsonElement input, string name) =>
        OptionalNumber(input, name) ?? throw new ValidationException($"'{name}' is required.");

    private static double? OptionalNumber(JsonElement input, string name)
    {
        if (Property(input, name) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
            throw new ValidationException($"'{name}' must be a number.");
        return number;
    }

    private static int RequiredInt(JsonElement input, string name)
    {
        var number = RequiredNumber(input, name);
        if (double.IsNaN(number) || number < int.MinValue || number > int.MaxValue)
            throw new ValidationException($"'{name}' is out of range.");
        return (int)Math.Round(number);
    }

    private static T RequiredEnum<T>(JsonElement input, string name) where T : struct, Enum =>
        OptionalEnum<T>(input, name) ?? throw new ValidationException($"'{name}' is required.");

    private static T? OptionalEnum<T>(JsonElement input, string name) where T : struct, Enum
    {
        if (OptionalString(input, name) is not { } text) return null;
        if (!Enum.TryParse<T>(text, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            throw new ValidationException($"'{text}' is not a valid {name}.");
        return parsed;
    }

    private static DateOnly? OptionalDate(JsonElement input, string name)
    {
        if (OptionalString(input, name) is not { } text) return null;
        if (!DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var date))
            throw new ValidationException($"'{name}' must be a date in yyyy-MM-dd form.");
        return date;
    }

    private static TimeOnly? OptionalTime(JsonElement input, string name)
    {
        if (OptionalString(input, name) is not { } text) return null;
        if (!TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var time))
            throw new ValidationException($"'{name}' must be a time in HH:mm form.");
        return time;
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement input, string name)
    {
        if (Property(input, name) is not { } value) return Array.Empty<JsonElement>();
        if (value.ValueKind != JsonValueKind.Array) throw new ValidationException($"'{name}' must be an array.");
        return value.EnumerateArray().ToList();
    }

    /// <summary>Reads a catalog-keyed nutrition array, dropping unknown ids and non-positive values.</summary>
    private static List<NutrientAmount> ReadNutrition(JsonElement input, string name)
    {
        var amounts = new List<NutrientAmount>();
        foreach (var entry in ReadArray(input, name))
        {
            var id = OptionalString(entry, "nutrientId");
            var value = OptionalNumber(entry, "value");
            if (id is null || value is not { } number || number <= 0 || !NutrientCatalog.IsKnown(id)) continue;
            amounts.Add(new NutrientAmount(id, number));
        }
        return amounts;
    }
}
