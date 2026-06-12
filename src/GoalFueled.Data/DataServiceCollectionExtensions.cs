using GoalFueled.Data.Repositories;
using GoalFueled.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoalFueled.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core context and repositories. Chooses the store based on configuration:
    /// <list type="bullet">
    /// <item><c>GoalFueled:UseInMemoryStore = true</c> (or no Cosmos connection) → in-memory provider.</item>
    /// <item>Otherwise → Cosmos provider using <c>GoalFueled:Cosmos:ConnectionString</c> (defaults to the
    /// local emulator) and <c>GoalFueled:Cosmos:Database</c>.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddGoalFueledData(this IServiceCollection services, IConfiguration config)
    {
        var useInMemory = string.Equals(config["GoalFueled:UseInMemoryStore"], "true", StringComparison.OrdinalIgnoreCase);
        var cosmosConnection = config["GoalFueled:Cosmos:ConnectionString"];
        var database = config["GoalFueled:Cosmos:Database"] ?? "goalfueled";

        if (useInMemory || string.IsNullOrWhiteSpace(cosmosConnection))
        {
            services.AddDbContext<GoalFueledDbContext>(o => o.UseInMemoryDatabase("goalfueled-dev"));
        }
        else
        {
            services.AddDbContext<GoalFueledDbContext>(o => o.UseCosmos(cosmosConnection, database));
        }

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IIngredientRepository, IngredientRepository>();
        services.AddScoped<IRecipeRepository, RecipeRepository>();
        services.AddScoped<IMealRepository, MealRepository>();
        services.AddScoped<IMealPlanRepository, MealPlanRepository>();
        services.AddScoped<IWeightEntryRepository, WeightEntryRepository>();
        services.AddScoped<IGoalRepository, GoalRepository>();
        services.AddScoped<IConsumptionEntryRepository, ConsumptionEntryRepository>();
        services.AddScoped<IBurnActivityRepository, BurnActivityRepository>();
        services.AddScoped<IUserStatsRepository, UserStatsRepository>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
