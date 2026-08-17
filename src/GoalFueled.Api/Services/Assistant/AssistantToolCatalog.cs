using GoalFueled.Domain;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Api.Services.Assistant;

/// <summary>
/// The tools the AI assistant may call, and their JSON schemas. Every tool maps 1:1 onto an existing
/// GoalFueled service method — the model never touches the database, so ownership, visibility, validation
/// and input limits all apply exactly as they do to the normal endpoints.
/// </summary>
/// <remarks>
/// Schemas constrain the model to this app's vocabulary (nutrient ids, units, fuel types come straight from
/// the catalogs/enums) so a hallucinated value is a schema violation rather than a bad write. The tool set
/// is deliberately small — a handful of clearly-bounded tools beats a sprawling surface.
/// </remarks>
public static class AssistantToolCatalog
{
    // ---- Read tools -------------------------------------------------------------------------------
    public const string SearchIngredients = "search_ingredients";
    public const string SearchRecipes = "search_recipes";
    public const string SearchFuelSources = "search_fuel_sources";
    public const string LookupNutrition = "lookup_ingredient_nutrition";

    // ---- Write tools ------------------------------------------------------------------------------
    public const string CreateIngredient = "create_ingredient";
    public const string CreateRecipe = "create_recipe";
    public const string CreateFuelSource = "create_fuel_source";
    public const string LogFuel = "log_fuel";
    public const string LogBurn = "log_burn";

    /// <summary>
    /// The "stop and ask" tool. It is NOT dispatched like the others: when the model calls it the loop
    /// pauses and the question is handed to the user, whose answer becomes this call's tool result on the
    /// next request. This is what makes "prompt if unsure" part of the protocol rather than a convention.
    /// </summary>
    public const string AskUserToConfirm = "ask_user_to_confirm";

    /// <summary>True when <paramref name="name"/> is a tool this catalog defines.</summary>
    public static bool IsKnown(string? name) => name is not null && Names.Contains(name);

    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        SearchIngredients, SearchRecipes, SearchFuelSources, LookupNutrition,
        CreateIngredient, CreateRecipe, CreateFuelSource, LogFuel, LogBurn, AskUserToConfirm,
    };

    /// <summary>How many results a search tool returns, bounding the tokens one call can add.</summary>
    public const int SearchResultLimit = 8;

    private static readonly string[] UnitNames = Enum.GetNames<UnitOfMeasure>();
    private static readonly string[] FuelTypeNames = Enum.GetNames<FuelType>();
    private static readonly string[] NutrientIds = NutrientCatalog.All.Select(n => n.Id).ToArray();

    /// <summary>A nutrition array schema, shared by the tools that accept catalog-keyed amounts.</summary>
    private static object NutritionSchema(string description) => new
    {
        type = "array",
        description,
        items = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "nutrientId", "value" },
            properties = new
            {
                nutrientId = new { type = "string", @enum = NutrientIds },
                value = new { type = "number", description = "Amount in the nutrient's catalog unit (kcal, g, mg, µg)." },
            },
        },
    };

    /// <summary>The tool definitions, in provider schema shape. Order is stable so prompt caching can work.</summary>
    public static readonly IReadOnlyList<object> Definitions = new object[]
    {
        new
        {
            name = SearchIngredients,
            description =
                "Search the user's visible ingredient library by name or alias. An ingredient is a single " +
                "food with per-serving nutrition (e.g. \"beef patty\", \"french fries\"). Call this before " +
                "creating an ingredient so you reuse what already exists. Returns up to " +
                SearchResultLimit + " matches with their ids.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "query" },
                properties = new
                {
                    query = new { type = "string", description = "Name to search for, e.g. \"french fries\"." },
                },
            },
        },
        new
        {
            name = SearchRecipes,
            description =
                "Search the user's visible recipes by name. A recipe composes ingredient quantities into a " +
                "dish and yields a number of servings. Call this before creating a recipe. Returns up to " +
                SearchResultLimit + " matches with their ids.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "query" },
                properties = new { query = new { type = "string" } },
            },
        },
        new
        {
            name = SearchFuelSources,
            description =
                "Search the user's visible fuel sources by name. A fuel source (a \"meal\" internally) is what " +
                "the user actually logs as fuel; it is made of portions of prepared recipes. ALWAYS call this " +
                "first when the user says they ate something — if a good match exists, log it instead of " +
                "creating anything. Returns up to " + SearchResultLimit + " matches with their ids.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "query" },
                properties = new { query = new { type = "string" } },
            },
        },
        new
        {
            name = LookupNutrition,
            description =
                "Look up real per-100 g nutrition for a food name from USDA FoodData Central (falling back to " +
                "the user's own catalog). Use this to ground a new ingredient's numbers instead of guessing. " +
                "May return notFound when the source is unavailable or has no match — say so rather than " +
                "silently inventing values.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "query" },
                properties = new { query = new { type = "string", description = "Food name, e.g. \"beef patty\"." } },
            },
        },
        new
        {
            name = CreateIngredient,
            description =
                "Create a private ingredient owned by this user. Only after searching found nothing suitable, " +
                "and only after the user has confirmed. Nutrition is per ONE serving of the size given.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "name", "unit", "servingSize", "nutrition" },
                properties = new
                {
                    name = new { type = "string" },
                    unit = new { type = "string", @enum = UnitNames, description = "Unit the serving size is measured in." },
                    servingSize = new { type = "number", description = "How much one serving is, in that unit (e.g. 100)." },
                    nutrition = NutritionSchema("Nutrition for ONE serving. Include at least calories."),
                },
            },
        },
        new
        {
            name = CreateRecipe,
            description =
                "Create a private recipe owned by this user from ingredients that already exist (search or " +
                "create them first). Only after the user has confirmed. Each item's unit must be compatible " +
                "with its ingredient's unit (mass with mass, volume with volume).",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "name", "servings", "items" },
                properties = new
                {
                    name = new { type = "string" },
                    servings = new { type = "number", description = "How many servings the whole recipe yields." },
                    fuelType = new { type = "string", @enum = FuelTypeNames, description = "Defaults to Food." },
                    items = new
                    {
                        type = "array",
                        description = "The ingredient lines making up the recipe.",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "ingredientId", "quantity", "unit" },
                            properties = new
                            {
                                ingredientId = new { type = "string", description = "Id from search_ingredients or create_ingredient." },
                                quantity = new { type = "number" },
                                unit = new { type = "string", @enum = UnitNames },
                            },
                        },
                    },
                },
            },
        },
        new
        {
            name = CreateFuelSource,
            description =
                "Create a private fuel source owned by this user from portions of existing recipes. This is " +
                "the thing the user logs as fuel. Only after the user has confirmed. Every portion's recipe " +
                "must already exist and match the fuel type.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "name", "portions" },
                properties = new
                {
                    name = new { type = "string" },
                    servings = new { type = "number", description = "How many servings the whole fuel source yields. Defaults to 1." },
                    fuelType = new { type = "string", @enum = FuelTypeNames, description = "Defaults to Food." },
                    portions = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "recipeId", "servings" },
                            properties = new
                            {
                                recipeId = new { type = "string", description = "Id from search_recipes or create_recipe." },
                                servings = new { type = "number", description = "Servings of that recipe in this fuel source." },
                            },
                        },
                    },
                },
            },
        },
        new
        {
            name = LogFuel,
            description =
                "Log fuel the user ate. Prefer a catalog fuel source: pass fuelSourceId and its nutrition is " +
                "computed live. Only when nothing suitable exists AND creating one is not warranted, pass a " +
                "name (plus optional per-serving nutrition) to log a free-form item instead.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "servings" },
                properties = new
                {
                    fuelSourceId = new { type = "string", description = "Id of an existing fuel source. Omit for a free-form entry." },
                    name = new { type = "string", description = "Required when fuelSourceId is omitted; otherwise an optional label override." },
                    servings = new { type = "number", description = "Servings eaten. Usually 1." },
                    date = new { type = "string", description = "Date eaten, yyyy-MM-dd. Defaults to today. Never in the future." },
                    nutrition = NutritionSchema("Per-serving nutrition for a free-form entry. Ignored when fuelSourceId is set."),
                },
            },
        },
        new
        {
            name = LogBurn,
            description = "Log a burn activity (exercise that used calories) for the user.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "name", "calories" },
                properties = new
                {
                    name = new { type = "string", description = "Activity name, e.g. \"Morning run\"." },
                    calories = new { type = "integer", description = "Calories burned, a positive number." },
                    date = new { type = "string", description = "Date, yyyy-MM-dd. Defaults to today. Never in the future." },
                    time = new { type = "string", description = "Optional time of day, HH:mm." },
                },
            },
        },
        new
        {
            name = AskUserToConfirm,
            description =
                "Stop and ask the user a yes/no or short-answer question, then wait. Call this ON ITS OWN " +
                "(no other tool in the same turn) whenever you are about to create an ingredient, recipe or " +
                "fuel source, or whenever you are not confident which existing item the user meant. State " +
                "plainly what you propose to create or log so the answer is informed.",
            input_schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "question" },
                properties = new
                {
                    question = new { type = "string", description = "The question, in plain language." },
                    details = new { type = "string", description = "Optional extra detail, e.g. the nutrition you propose." },
                },
            },
        },
    };
}
