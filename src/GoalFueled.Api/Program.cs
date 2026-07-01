using Azure.Monitor.OpenTelemetry.Exporter;
using GoalFueled.Api.Auth;
using GoalFueled.Api.Services;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Api.Services.OpenFoodFacts;
using GoalFueled.Data;
using GoalFueled.Data.Seeding;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

// --- Application services ---
builder.Services.AddGoalFueledData(builder.Configuration);

// Identity providers (first to resolve wins). DevIdentityProvider is inert unless EnableDevAuth=true,
// so in production only Entra (Google sign-in via Entra External ID) resolves real callers.
builder.Services.AddSingleton<IIdentityProvider, DevIdentityProvider>();
builder.Services.AddSingleton<IIdentityProvider, EntraExternalIdProvider>();

builder.Services.AddScoped<IAuthenticator, Authenticator>();
builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddScoped<IngredientService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<MealService>();
builder.Services.AddScoped<MealPlanService>();
builder.Services.AddScoped<WeightService>();
builder.Services.AddScoped<GoalService>();
builder.Services.AddScoped<ConsumptionService>();
builder.Services.AddScoped<BurnService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<GamificationAwardService>();
builder.Services.AddScoped<GamificationService>();
builder.Services.AddScoped<UserAdminService>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.AddScoped<FavoriteService>();
builder.Services.AddScoped<SharingService>();

// Automatic ingredient nutrition lookup (USDA FoodData Central). The HTTP call is done server-side
// via IHttpClientFactory so the API key stays off the client and CORS is a non-issue.
builder.Services.AddHttpClient<IFdcNutritionClient, FdcNutritionClient>(http =>
    http.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<NutritionLookupService>();

// Barcode scan → Open Food Facts (read for lookup, write to contribute new products back). Same
// server-side IHttpClientFactory pattern as USDA so credentials stay off the client.
builder.Services.AddHttpClient<IOffClient, OffClient>(http =>
    http.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<BarcodeLookupService>();
builder.Services.AddScoped<OffContributionService>();

// Nutrition-label OCR fallback (Claude vision). Longer timeout — a vision call takes longer than a
// plain REST lookup.
builder.Services.AddHttpClient<INutritionLabelReader, AnthropicVisionClient>(http =>
    http.Timeout = TimeSpan.FromSeconds(60));
// Admin runtime override for the OCR model (for A/B testing before committing a choice to config).
builder.Services.AddScoped<VisionModelSettingsService>();

var host = builder.Build();

// Ensure the database/containers exist and seed starter data. Resilient: if the configured store
// (e.g. the Cosmos emulator) is unreachable, log and continue so the host still starts.
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        logger.LogInformation("Database initialized.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed. Is the data store running? " +
            "Set GoalFueled:UseInMemoryStore=true to run without Cosmos.");
    }
}

host.Run();
