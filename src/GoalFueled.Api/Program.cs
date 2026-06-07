using Azure.Monitor.OpenTelemetry.Exporter;
using GoalFueled.Api.Auth;
using GoalFueled.Api.Services;
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

// Identity providers (first to resolve wins). Dev auth now; Entra External ID seam for later.
builder.Services.AddSingleton<IIdentityProvider, DevIdentityProvider>();
// builder.Services.AddSingleton<IIdentityProvider, EntraExternalIdProvider>(); // enable for real social login

builder.Services.AddScoped<IAuthenticator, Authenticator>();
builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddScoped<IngredientService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<MealService>();
builder.Services.AddScoped<MealPlanService>();
builder.Services.AddScoped<WeightService>();
builder.Services.AddScoped<UserAdminService>();

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
