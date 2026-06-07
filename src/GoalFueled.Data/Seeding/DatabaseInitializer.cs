using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Seeding;

/// <summary>
/// Ensures the database/containers exist and seeds a starter set of app-level ingredients so a
/// fresh install has something to browse. Safe to run on every startup — it no-ops if data exists.
/// </summary>
public class DatabaseInitializer
{
    private readonly GoalFueledDbContext _db;

    public DatabaseInitializer(GoalFueledDbContext db) => _db = db;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Creates the Cosmos database + containers (or the in-memory store). Idempotent.
        await _db.Database.EnsureCreatedAsync(ct);

        if (await _db.Ingredients.AnyAsync(ct)) return;

        _db.Ingredients.AddRange(SeedIngredients());
        await _db.SaveChangesAsync(ct);
    }

    private static IEnumerable<Ingredient> SeedIngredients()
    {
        // Values are approximate, per the stated serving size. They exist to give the app
        // realistic sample data, not to be a nutrition authority.
        yield return App("Chicken Breast (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 165, ["protein"] = 31, ["total_fat"] = 3.6, ["saturated_fat"] = 1,
            ["cholesterol"] = 85, ["sodium"] = 74, ["potassium"] = 256
        });

        yield return App("White Rice (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 130, ["protein"] = 2.7, ["total_carbohydrate"] = 28, ["dietary_fiber"] = 0.4,
            ["total_fat"] = 0.3, ["sodium"] = 1, ["iron"] = 0.2
        });

        yield return App("Broccoli (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 34, ["protein"] = 2.8, ["total_carbohydrate"] = 7, ["dietary_fiber"] = 2.6,
            ["total_sugars"] = 1.7, ["vitamin_c"] = 89, ["calcium"] = 47, ["potassium"] = 316
        });

        yield return App("Olive Oil", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 119, ["total_fat"] = 13.5, ["saturated_fat"] = 1.9,
            ["monounsaturated_fat"] = 9.9, ["vitamin_e"] = 1.9, ["vitamin_k"] = 8.1
        });

        yield return App("Large Egg", UnitOfMeasure.Piece, 1, new()
        {
            ["calories"] = 72, ["protein"] = 6.3, ["total_fat"] = 4.8, ["saturated_fat"] = 1.6,
            ["cholesterol"] = 186, ["sodium"] = 71, ["vitamin_d"] = 1.1, ["vitamin_b12"] = 0.6
        });

        yield return App("Whole Milk", UnitOfMeasure.Cup, 1, new()
        {
            ["calories"] = 149, ["protein"] = 7.7, ["total_carbohydrate"] = 12, ["total_sugars"] = 12,
            ["total_fat"] = 8, ["saturated_fat"] = 4.6, ["calcium"] = 276, ["vitamin_d"] = 3.2
        });

        yield return App("Banana", UnitOfMeasure.Piece, 1, new()
        {
            ["calories"] = 105, ["protein"] = 1.3, ["total_carbohydrate"] = 27, ["dietary_fiber"] = 3.1,
            ["total_sugars"] = 14, ["potassium"] = 422, ["vitamin_c"] = 10.3, ["vitamin_b6"] = 0.4
        });

        yield return App("Water", UnitOfMeasure.FluidOunce, 8, new()
        {
            ["calories"] = 0, ["water"] = 237
        });
    }

    private static Ingredient App(string name, UnitOfMeasure unit, double serving,
        Dictionary<string, double> nutrients) => new()
    {
        Name = name,
        Unit = unit,
        ServingSize = serving,
        Scope = OwnershipScope.App,
        OwnerUserId = null,
        NutritionPerServing = nutrients
            .Where(kv => NutrientCatalog.IsKnown(kv.Key))
            .Select(kv => new NutrientAmount(kv.Key, kv.Value))
            .ToList()
    };
}
