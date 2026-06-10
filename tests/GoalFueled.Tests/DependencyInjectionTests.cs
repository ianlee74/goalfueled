using GoalFueled.Api.Auth;
using GoalFueled.Api.Services;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Data.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoalFueled.Tests;

/// <summary>
/// Verifies that the service graph registered by Program.cs actually resolves — catching DI
/// misconfiguration (missing registrations, captive dependencies) that the host would only hit at runtime.
/// </summary>
public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoalFueled:UseInMemoryStore"] = "true",
                ["GoalFueled:Auth:EnableDevAuth"] = "true",
                // ImageStorageService reads this at construction; Azurite's well-known dev value is fine here
                // (no blob I/O happens — the client is only created, never connected).
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // The Functions host registers IConfiguration for us; provide it here so the graph matches.
        services.AddSingleton<IConfiguration>(config);

        // Mirror of Program.cs application registrations.
        services.AddGoalFueledData(config);
        services.AddSingleton<IIdentityProvider, DevIdentityProvider>();
        services.AddScoped<IAuthenticator, Authenticator>();
        services.AddSingleton<ImageStorageService>();
        services.AddScoped<IngredientService>();
        services.AddScoped<RecipeService>();
        services.AddScoped<MealService>();
        services.AddScoped<MealPlanService>();
        services.AddScoped<WeightService>();
        services.AddScoped<GoalService>();
        services.AddScoped<ConsumptionService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<UserProfileService>();
        services.AddHttpClient<IFdcNutritionClient, FdcNutritionClient>();
        services.AddScoped<NutritionLookupService>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    [Fact]
    public void All_core_services_resolve()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IAuthenticator>());
        Assert.NotNull(sp.GetRequiredService<ImageStorageService>());
        Assert.NotNull(sp.GetRequiredService<IngredientService>());
        Assert.NotNull(sp.GetRequiredService<RecipeService>());
        Assert.NotNull(sp.GetRequiredService<MealService>());
        Assert.NotNull(sp.GetRequiredService<MealPlanService>());
        Assert.NotNull(sp.GetRequiredService<WeightService>());
        Assert.NotNull(sp.GetRequiredService<GoalService>());
        Assert.NotNull(sp.GetRequiredService<ConsumptionService>());
        Assert.NotNull(sp.GetRequiredService<UserAdminService>());
        Assert.NotNull(sp.GetRequiredService<UserProfileService>());
        Assert.NotNull(sp.GetRequiredService<NutritionLookupService>());
        Assert.NotNull(sp.GetRequiredService<IFdcNutritionClient>());
        Assert.NotNull(sp.GetRequiredService<IUserRepository>());
        Assert.NotNull(sp.GetRequiredService<IIngredientRepository>());
        Assert.NotNull(sp.GetRequiredService<IRecipeRepository>());
        Assert.NotNull(sp.GetRequiredService<IMealRepository>());
        Assert.NotNull(sp.GetRequiredService<IMealPlanRepository>());
        Assert.NotNull(sp.GetRequiredService<IWeightEntryRepository>());
        Assert.NotNull(sp.GetRequiredService<IGoalRepository>());
        Assert.NotNull(sp.GetRequiredService<IConsumptionEntryRepository>());
        Assert.NotNull(sp.GetRequiredService<DatabaseInitializer>());
        Assert.NotEmpty(sp.GetServices<IIdentityProvider>());
    }

    [Fact]
    public async Task Database_initializer_seeds_app_ingredients()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

        var db = scope.ServiceProvider.GetRequiredService<GoalFueledDbContext>();
        Assert.True(db.Ingredients.Any(), "Expected seeded app-level ingredients.");
    }
}
