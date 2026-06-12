using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Seeding;

/// <summary>
/// Ensures the database/containers exist and seeds a starter set of app-level ingredients so a
/// fresh install has something to browse. Safe to run on every startup — it adds only the seed
/// ingredients that aren't already present, so existing databases pick up newly-added ones too.
/// Already-seeded ingredients that predate the finder's search metadata (tags/aliases/non-English
/// names) get that metadata backfilled, but rows the user may have edited are otherwise untouched.
/// </summary>
public class DatabaseInitializer
{
    private readonly GoalFueledDbContext _db;

    public DatabaseInitializer(GoalFueledDbContext db) => _db = db;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Creates the Cosmos database + containers (or the in-memory store). Idempotent.
        await _db.Database.EnsureCreatedAsync(ct);

        // Load the app-scoped ingredients that already exist, so we add only what's missing.
        // This lets an already-seeded database gain newly-added seed ingredients on a later startup,
        // while never duplicating existing rows or touching user-authored data. The Scope equality is a
        // translatable predicate, but anything richer must run in memory — the Cosmos provider can't
        // translate a scalar Select (see IngredientRepository.ListVisibleAsync), so materialize first.
        var existingByName = new Dictionary<string, Ingredient>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in await _db.Ingredients
            .Where(i => i.Scope == OwnershipScope.App)
            .ToListAsync(ct))
        {
            existingByName.TryAdd(existing.Name, existing);
        }

        var seeds = SeedIngredients().ToList();
        var toAdd = seeds.Where(s => !existingByName.ContainsKey(s.Name)).ToList();

        // Backfill finder metadata onto already-seeded app ingredients written before the search
        // fields existed. Only rows with NO search metadata at all are touched, so any tags/aliases
        // an admin later curates are never overwritten.
        var backfilled = false;
        foreach (var seed in seeds)
        {
            if (!existingByName.TryGetValue(seed.Name, out var existing)) continue;
            if (existing.Tags is not null || existing.Aliases is not null || existing.LocalizedNames is not null) continue;
            if (seed.Tags is null && seed.Aliases is null && seed.LocalizedNames is null) continue;
            existing.Tags = seed.Tags;
            existing.Aliases = seed.Aliases;
            existing.LocalizedNames = seed.LocalizedNames;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            backfilled = true;
        }

        if (toAdd.Count == 0 && !backfilled) return;

        _db.Ingredients.AddRange(toAdd);
        await _db.SaveChangesAsync(ct);
    }

    private static IEnumerable<Ingredient> SeedIngredients()
    {
        // Values are approximate, per the stated serving size. They exist to give the app
        // realistic sample data, not to be a nutrition authority. Tags/aliases/non-English names
        // feed the ingredient finder: tags double as browse categories, aliases cover common
        // alternate names, and localized names let users search in their own language.
        yield return App("Chicken Breast (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 165, ["protein"] = 31, ["total_fat"] = 3.6, ["saturated_fat"] = 1,
            ["cholesterol"] = 85, ["sodium"] = 74, ["potassium"] = 256
        },
        tags: new[] { "protein", "meat", "poultry" },
        aliases: new[] { "chicken fillet" },
        names: new[] { ("es", "pechuga de pollo"), ("fr", "blanc de poulet") });

        yield return App("White Rice (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 130, ["protein"] = 2.7, ["total_carbohydrate"] = 28, ["dietary_fiber"] = 0.4,
            ["total_fat"] = 0.3, ["sodium"] = 1, ["iron"] = 0.2
        },
        tags: new[] { "grain", "starch" },
        aliases: new[] { "steamed rice" },
        names: new[] { ("es", "arroz blanco"), ("fr", "riz blanc"), ("zh", "米饭") });

        yield return App("Broccoli (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 34, ["protein"] = 2.8, ["total_carbohydrate"] = 7, ["dietary_fiber"] = 2.6,
            ["total_sugars"] = 1.7, ["vitamin_c"] = 89, ["calcium"] = 47, ["potassium"] = 316
        },
        tags: new[] { "vegetable", "low calorie" },
        names: new[] { ("es", "brócoli"), ("fr", "brocoli") });

        yield return App("Olive Oil", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 119, ["total_fat"] = 13.5, ["saturated_fat"] = 1.9,
            ["monounsaturated_fat"] = 9.9, ["vitamin_e"] = 1.9, ["vitamin_k"] = 8.1
        },
        tags: new[] { "fat", "oil" },
        aliases: new[] { "extra virgin olive oil", "EVOO" },
        names: new[] { ("es", "aceite de oliva"), ("fr", "huile d'olive"), ("it", "olio d'oliva") });

        yield return App("Large Egg", UnitOfMeasure.Piece, 1, new()
        {
            ["calories"] = 72, ["protein"] = 6.3, ["total_fat"] = 4.8, ["saturated_fat"] = 1.6,
            ["cholesterol"] = 186, ["sodium"] = 71, ["vitamin_d"] = 1.1, ["vitamin_b12"] = 0.6
        },
        tags: new[] { "protein", "egg", "breakfast" },
        aliases: new[] { "egg" },
        names: new[] { ("es", "huevo"), ("fr", "œuf"), ("de", "Ei") });

        yield return App("Whole Milk", UnitOfMeasure.Cup, 1, new()
        {
            ["calories"] = 149, ["protein"] = 7.7, ["total_carbohydrate"] = 12, ["total_sugars"] = 12,
            ["total_fat"] = 8, ["saturated_fat"] = 4.6, ["calcium"] = 276, ["vitamin_d"] = 3.2
        },
        tags: new[] { "dairy", "beverage" },
        names: new[] { ("es", "leche entera"), ("fr", "lait entier") });

        yield return App("Banana", UnitOfMeasure.Piece, 1, new()
        {
            ["calories"] = 105, ["protein"] = 1.3, ["total_carbohydrate"] = 27, ["dietary_fiber"] = 3.1,
            ["total_sugars"] = 14, ["potassium"] = 422, ["vitamin_c"] = 10.3, ["vitamin_b6"] = 0.4
        },
        tags: new[] { "fruit", "snack" },
        names: new[] { ("es", "plátano"), ("fr", "banane") });

        yield return App("Water", UnitOfMeasure.FluidOunce, 8, new()
        {
            ["calories"] = 0, ["water"] = 237
        },
        tags: new[] { "beverage", "zero calorie" },
        names: new[] { ("es", "agua"), ("fr", "eau") });

        // ---------------------------------------------------------------------------------------
        // Common U.S. ingredients. Values are USDA FoodData Central reference figures, per the
        // stated serving size (whole foods per 100 g; volume/count items per natural serving).
        // ---------------------------------------------------------------------------------------

        // ---- Meat & poultry (per 100 g, cooked) ----
        yield return App("Ground Beef, 80/20 (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 254, ["protein"] = 25.7, ["total_fat"] = 16.2, ["saturated_fat"] = 6.4,
            ["cholesterol"] = 86, ["sodium"] = 75, ["potassium"] = 318, ["iron"] = 2.4,
            ["zinc"] = 5.9, ["vitamin_b12"] = 2.4
        },
        tags: new[] { "protein", "meat", "beef" },
        aliases: new[] { "hamburger meat", "minced beef" },
        names: new[] { ("es", "carne molida"), ("fr", "bœuf haché") });

        yield return App("Beef Sirloin Steak (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 206, ["protein"] = 28, ["total_fat"] = 9.6, ["saturated_fat"] = 3.6,
            ["cholesterol"] = 80, ["sodium"] = 53, ["potassium"] = 330, ["iron"] = 1.6,
            ["zinc"] = 4.6, ["vitamin_b12"] = 1.5
        },
        tags: new[] { "protein", "meat", "beef" },
        aliases: new[] { "sirloin" },
        names: new[] { ("es", "solomillo de res"), ("fr", "bifteck") });

        yield return App("Pork Chop (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 231, ["protein"] = 26, ["total_fat"] = 13, ["saturated_fat"] = 4.7,
            ["cholesterol"] = 78, ["sodium"] = 62, ["potassium"] = 360, ["thiamin"] = 0.6,
            ["zinc"] = 2.2
        },
        tags: new[] { "protein", "meat", "pork" },
        names: new[] { ("es", "chuleta de cerdo"), ("fr", "côtelette de porc") });

        yield return App("Bacon (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 541, ["protein"] = 37, ["total_fat"] = 42, ["saturated_fat"] = 14,
            ["cholesterol"] = 110, ["sodium"] = 1717, ["potassium"] = 565, ["selenium"] = 52
        },
        tags: new[] { "protein", "meat", "pork", "breakfast" },
        names: new[] { ("es", "tocino"), ("de", "Speck") });

        yield return App("Chicken Thigh, skinless (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 209, ["protein"] = 26, ["total_fat"] = 10.9, ["saturated_fat"] = 3,
            ["cholesterol"] = 134, ["sodium"] = 88, ["potassium"] = 240, ["zinc"] = 2.5
        },
        tags: new[] { "protein", "meat", "poultry" },
        names: new[] { ("es", "muslo de pollo"), ("fr", "haut de cuisse de poulet") });

        yield return App("Turkey Breast (roasted)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 135, ["protein"] = 30, ["total_fat"] = 1, ["cholesterol"] = 67,
            ["sodium"] = 52, ["potassium"] = 239, ["niacin"] = 9, ["selenium"] = 27
        },
        tags: new[] { "protein", "meat", "poultry" },
        names: new[] { ("es", "pechuga de pavo"), ("fr", "blanc de dinde") });

        yield return App("Ground Turkey (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 203, ["protein"] = 27, ["total_fat"] = 10, ["saturated_fat"] = 2.6,
            ["cholesterol"] = 93, ["sodium"] = 91, ["potassium"] = 280, ["zinc"] = 3.5
        },
        tags: new[] { "protein", "meat", "poultry" },
        aliases: new[] { "minced turkey" },
        names: new[] { ("es", "pavo molido") });

        // ---- Seafood (per 100 g, cooked unless noted) ----
        yield return App("Salmon, Atlantic (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 206, ["protein"] = 22, ["total_fat"] = 12, ["saturated_fat"] = 3.1,
            ["cholesterol"] = 63, ["sodium"] = 61, ["potassium"] = 384, ["vitamin_d"] = 11,
            ["vitamin_b12"] = 2.8, ["selenium"] = 41
        },
        tags: new[] { "protein", "seafood", "fish" },
        names: new[] { ("es", "salmón"), ("fr", "saumon"), ("ja", "鮭") });

        yield return App("Tuna, canned in water (drained)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 116, ["protein"] = 26, ["total_fat"] = 0.8, ["cholesterol"] = 30,
            ["sodium"] = 247, ["selenium"] = 66, ["niacin"] = 11, ["vitamin_b12"] = 2.5
        },
        tags: new[] { "protein", "seafood", "fish", "canned" },
        aliases: new[] { "canned tuna" },
        names: new[] { ("es", "atún"), ("fr", "thon") });

        yield return App("Shrimp (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 99, ["protein"] = 24, ["total_fat"] = 0.3, ["cholesterol"] = 189,
            ["sodium"] = 111, ["selenium"] = 40, ["vitamin_b12"] = 1.4
        },
        tags: new[] { "protein", "seafood", "shellfish" },
        aliases: new[] { "prawns" },
        names: new[] { ("es", "camarones"), ("fr", "crevettes") });

        yield return App("Tilapia (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 128, ["protein"] = 26, ["total_fat"] = 2.7, ["cholesterol"] = 57,
            ["sodium"] = 56, ["potassium"] = 380, ["selenium"] = 54, ["vitamin_b12"] = 1.9
        },
        tags: new[] { "protein", "seafood", "fish" });

        yield return App("Cod (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 105, ["protein"] = 22.8, ["total_fat"] = 0.9, ["cholesterol"] = 55,
            ["sodium"] = 78, ["potassium"] = 244, ["selenium"] = 37, ["vitamin_b12"] = 1.1
        },
        tags: new[] { "protein", "seafood", "fish" },
        names: new[] { ("es", "bacalao"), ("fr", "cabillaud") });

        // ---- Dairy & cheese ----
        yield return App("Cheddar Cheese", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 403, ["protein"] = 23, ["total_fat"] = 33, ["saturated_fat"] = 19,
            ["cholesterol"] = 99, ["sodium"] = 653, ["calcium"] = 710, ["phosphorus"] = 512
        },
        tags: new[] { "dairy", "cheese" },
        names: new[] { ("es", "queso cheddar") });

        yield return App("Mozzarella Cheese, part-skim", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 254, ["protein"] = 24, ["total_fat"] = 16, ["saturated_fat"] = 10,
            ["cholesterol"] = 64, ["sodium"] = 619, ["calcium"] = 731, ["phosphorus"] = 463
        },
        tags: new[] { "dairy", "cheese" },
        names: new[] { ("es", "queso mozzarella") });

        yield return App("Parmesan Cheese (grated)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 420, ["protein"] = 38, ["total_fat"] = 28, ["saturated_fat"] = 17,
            ["cholesterol"] = 88, ["sodium"] = 1804, ["calcium"] = 1184, ["phosphorus"] = 694
        },
        tags: new[] { "dairy", "cheese" },
        aliases: new[] { "parmigiano" },
        names: new[] { ("it", "parmigiano reggiano"), ("fr", "parmesan") });

        yield return App("Cream Cheese", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 51, ["protein"] = 0.9, ["total_fat"] = 5.1, ["saturated_fat"] = 2.9,
            ["cholesterol"] = 15, ["sodium"] = 48, ["vitamin_a"] = 46
        },
        tags: new[] { "dairy", "cheese", "spread" },
        names: new[] { ("es", "queso crema"), ("fr", "fromage à la crème") });

        yield return App("Cottage Cheese, 2% lowfat", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 84, ["protein"] = 11, ["total_carbohydrate"] = 4.3, ["total_sugars"] = 4.3,
            ["total_fat"] = 2.3, ["cholesterol"] = 12, ["sodium"] = 330, ["calcium"] = 91
        },
        tags: new[] { "dairy", "cheese", "protein" },
        aliases: new[] { "curd cheese" },
        names: new[] { ("es", "requesón") });

        yield return App("Greek Yogurt, plain nonfat", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 59, ["protein"] = 10, ["total_carbohydrate"] = 3.6, ["total_sugars"] = 3.2,
            ["total_fat"] = 0.4, ["sodium"] = 36, ["calcium"] = 110
        },
        tags: new[] { "dairy", "yogurt", "protein", "breakfast" },
        aliases: new[] { "strained yogurt" },
        names: new[] { ("es", "yogur griego"), ("fr", "yaourt grec") });

        yield return App("Plain Yogurt, whole milk", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 61, ["protein"] = 3.5, ["total_carbohydrate"] = 4.7, ["total_sugars"] = 4.7,
            ["total_fat"] = 3.3, ["saturated_fat"] = 2.1, ["cholesterol"] = 13, ["calcium"] = 121
        },
        tags: new[] { "dairy", "yogurt", "breakfast" },
        names: new[] { ("es", "yogur natural"), ("fr", "yaourt nature") });

        yield return App("Butter, salted", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 102, ["total_fat"] = 11.5, ["saturated_fat"] = 7.3, ["cholesterol"] = 31,
            ["sodium"] = 91, ["vitamin_a"] = 97
        },
        tags: new[] { "dairy", "fat", "baking" },
        names: new[] { ("es", "mantequilla"), ("fr", "beurre"), ("de", "Butter") });

        // ---- Grains, breads & starches ----
        yield return App("Brown Rice (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 123, ["protein"] = 2.7, ["total_carbohydrate"] = 26, ["dietary_fiber"] = 1.6,
            ["total_fat"] = 1, ["sodium"] = 4, ["magnesium"] = 39, ["manganese"] = 1.1
        },
        tags: new[] { "grain", "starch", "whole grain" },
        names: new[] { ("es", "arroz integral"), ("fr", "riz complet") });

        yield return App("Pasta (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 158, ["protein"] = 5.8, ["total_carbohydrate"] = 31, ["dietary_fiber"] = 1.8,
            ["total_fat"] = 0.9, ["sodium"] = 1, ["selenium"] = 26, ["iron"] = 0.5
        },
        tags: new[] { "grain", "starch" },
        aliases: new[] { "noodles", "spaghetti" },
        names: new[] { ("fr", "pâtes") });

        yield return App("Quinoa (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 120, ["protein"] = 4.4, ["total_carbohydrate"] = 21, ["dietary_fiber"] = 2.8,
            ["total_fat"] = 1.9, ["magnesium"] = 64, ["iron"] = 1.5, ["folate"] = 42
        },
        tags: new[] { "grain", "whole grain", "protein" },
        names: new[] { ("es", "quinua") });

        yield return App("Rolled Oats (dry)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 389, ["protein"] = 17, ["total_carbohydrate"] = 66, ["dietary_fiber"] = 11,
            ["total_fat"] = 6.9, ["iron"] = 4.7, ["magnesium"] = 177, ["zinc"] = 4, ["thiamin"] = 0.76
        },
        tags: new[] { "grain", "whole grain", "breakfast" },
        aliases: new[] { "oatmeal", "porridge oats" },
        names: new[] { ("es", "avena"), ("fr", "flocons d'avoine") });

        yield return App("All-Purpose Flour", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 364, ["protein"] = 10, ["total_carbohydrate"] = 76, ["dietary_fiber"] = 2.7,
            ["total_fat"] = 1, ["sodium"] = 2, ["iron"] = 4.6, ["folate"] = 291, ["thiamin"] = 0.8
        },
        tags: new[] { "grain", "baking" },
        aliases: new[] { "plain flour", "AP flour" },
        names: new[] { ("es", "harina de trigo"), ("fr", "farine de blé") });

        yield return App("White Bread", UnitOfMeasure.Piece, 1, new()
        {
            ["calories"] = 67, ["protein"] = 1.9, ["total_carbohydrate"] = 13, ["dietary_fiber"] = 0.6,
            ["total_sugars"] = 1.4, ["total_fat"] = 0.8, ["sodium"] = 127, ["iron"] = 0.9, ["calcium"] = 38
        },
        tags: new[] { "grain", "bread" },
        aliases: new[] { "sandwich bread" },
        names: new[] { ("es", "pan blanco"), ("fr", "pain blanc") });

        yield return App("Whole Wheat Bread", UnitOfMeasure.Piece, 1, new()
        {
            ["calories"] = 69, ["protein"] = 3.6, ["total_carbohydrate"] = 12, ["dietary_fiber"] = 1.9,
            ["total_sugars"] = 1.4, ["total_fat"] = 1, ["sodium"] = 132, ["iron"] = 0.7, ["magnesium"] = 23
        },
        tags: new[] { "grain", "bread", "whole grain" },
        aliases: new[] { "wholemeal bread" },
        names: new[] { ("es", "pan integral"), ("fr", "pain complet") });

        yield return App("Corn Tortilla", UnitOfMeasure.Piece, 1, new()
        {
            ["calories"] = 52, ["protein"] = 1.4, ["total_carbohydrate"] = 11, ["dietary_fiber"] = 1.5,
            ["total_fat"] = 0.7, ["sodium"] = 11, ["calcium"] = 20, ["magnesium"] = 17
        },
        tags: new[] { "grain", "bread" },
        names: new[] { ("es", "tortilla de maíz") });

        yield return App("Potato, baked with skin", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 93, ["protein"] = 2.5, ["total_carbohydrate"] = 21, ["dietary_fiber"] = 2.2,
            ["total_fat"] = 0.1, ["potassium"] = 535, ["vitamin_c"] = 9.6, ["vitamin_b6"] = 0.3,
            ["magnesium"] = 28
        },
        tags: new[] { "vegetable", "starch" },
        aliases: new[] { "baked potato" },
        names: new[] { ("es", "patata asada"), ("fr", "pomme de terre") });

        yield return App("Sweet Potato, baked", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 90, ["protein"] = 2, ["total_carbohydrate"] = 21, ["dietary_fiber"] = 3.3,
            ["total_sugars"] = 6.5, ["total_fat"] = 0.2, ["vitamin_a"] = 961, ["potassium"] = 475,
            ["vitamin_c"] = 19.6
        },
        tags: new[] { "vegetable", "starch" },
        aliases: new[] { "yam" },
        names: new[] { ("es", "batata"), ("es", "camote"), ("fr", "patate douce") });

        // ---- Legumes, nuts & seeds ----
        yield return App("Black Beans (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 132, ["protein"] = 8.9, ["total_carbohydrate"] = 24, ["dietary_fiber"] = 8.7,
            ["total_fat"] = 0.5, ["sodium"] = 1, ["iron"] = 2.1, ["magnesium"] = 70, ["folate"] = 149,
            ["potassium"] = 355
        },
        tags: new[] { "legume", "protein", "fiber" },
        aliases: new[] { "turtle beans" },
        names: new[] { ("es", "frijoles negros"), ("fr", "haricots noirs") });

        yield return App("Chickpeas (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 164, ["protein"] = 8.9, ["total_carbohydrate"] = 27, ["dietary_fiber"] = 7.6,
            ["total_fat"] = 2.6, ["iron"] = 2.9, ["magnesium"] = 48, ["folate"] = 172, ["zinc"] = 1.5
        },
        tags: new[] { "legume", "protein", "fiber" },
        aliases: new[] { "garbanzo beans" },
        names: new[] { ("es", "garbanzos"), ("fr", "pois chiches") });

        yield return App("Lentils (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 116, ["protein"] = 9, ["total_carbohydrate"] = 20, ["dietary_fiber"] = 7.9,
            ["total_fat"] = 0.4, ["iron"] = 3.3, ["folate"] = 181, ["potassium"] = 369
        },
        tags: new[] { "legume", "protein", "fiber" },
        aliases: new[] { "dal" },
        names: new[] { ("es", "lentejas"), ("fr", "lentilles"), ("hi", "दाल") });

        yield return App("Peanut Butter", UnitOfMeasure.Tablespoon, 2, new()
        {
            ["calories"] = 188, ["protein"] = 8, ["total_carbohydrate"] = 6.9, ["dietary_fiber"] = 1.9,
            ["total_sugars"] = 3.4, ["total_fat"] = 16, ["saturated_fat"] = 3.3, ["sodium"] = 152,
            ["magnesium"] = 51, ["niacin"] = 4.2
        },
        tags: new[] { "nuts", "spread", "protein" },
        names: new[] { ("es", "mantequilla de maní"), ("fr", "beurre de cacahuète") });

        yield return App("Almonds", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 579, ["protein"] = 21, ["total_carbohydrate"] = 22, ["dietary_fiber"] = 12.5,
            ["total_fat"] = 50, ["saturated_fat"] = 3.8, ["vitamin_e"] = 25.6, ["magnesium"] = 270,
            ["calcium"] = 269, ["iron"] = 3.7
        },
        tags: new[] { "nuts", "snack" },
        names: new[] { ("es", "almendras"), ("fr", "amandes") });

        yield return App("Walnuts", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 654, ["protein"] = 15, ["total_carbohydrate"] = 14, ["dietary_fiber"] = 6.7,
            ["total_fat"] = 65, ["saturated_fat"] = 6.1, ["polyunsaturated_fat"] = 47.2,
            ["magnesium"] = 158, ["manganese"] = 3.4
        },
        tags: new[] { "nuts", "snack" },
        names: new[] { ("es", "nueces"), ("fr", "noix") });

        yield return App("Peanuts", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 567, ["protein"] = 26, ["total_carbohydrate"] = 16, ["dietary_fiber"] = 8.5,
            ["total_fat"] = 49, ["saturated_fat"] = 6.3, ["magnesium"] = 168, ["niacin"] = 12.1,
            ["folate"] = 240
        },
        tags: new[] { "nuts", "snack", "legume" },
        aliases: new[] { "groundnuts" },
        names: new[] { ("es", "cacahuetes"), ("es", "maní"), ("fr", "cacahuètes") });

        yield return App("Cashews", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 553, ["protein"] = 18, ["total_carbohydrate"] = 30, ["dietary_fiber"] = 3.3,
            ["total_fat"] = 44, ["saturated_fat"] = 7.8, ["magnesium"] = 292, ["iron"] = 6.7,
            ["zinc"] = 5.8
        },
        tags: new[] { "nuts", "snack" },
        names: new[] { ("es", "anacardos"), ("fr", "noix de cajou") });

        yield return App("Chia Seeds", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 486, ["protein"] = 17, ["total_carbohydrate"] = 42, ["dietary_fiber"] = 34,
            ["total_fat"] = 31, ["saturated_fat"] = 3.3, ["calcium"] = 631, ["magnesium"] = 335,
            ["iron"] = 7.7
        },
        tags: new[] { "seeds", "fiber" },
        names: new[] { ("es", "semillas de chía"), ("fr", "graines de chia") });

        // ---- Vegetables (per 100 g, raw unless noted) ----
        yield return App("Carrot (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 41, ["protein"] = 0.9, ["total_carbohydrate"] = 10, ["dietary_fiber"] = 2.8,
            ["total_sugars"] = 4.7, ["vitamin_a"] = 835, ["potassium"] = 320, ["vitamin_k"] = 13
        },
        tags: new[] { "vegetable", "low calorie" },
        names: new[] { ("es", "zanahoria"), ("fr", "carotte") });

        yield return App("Spinach (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 23, ["protein"] = 2.9, ["total_carbohydrate"] = 3.6, ["dietary_fiber"] = 2.2,
            ["vitamin_a"] = 469, ["vitamin_k"] = 483, ["folate"] = 194, ["iron"] = 2.7, ["magnesium"] = 79
        },
        tags: new[] { "vegetable", "leafy green", "low calorie" },
        names: new[] { ("es", "espinacas"), ("fr", "épinards") });

        yield return App("Tomato (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 18, ["protein"] = 0.9, ["total_carbohydrate"] = 3.9, ["dietary_fiber"] = 1.2,
            ["total_sugars"] = 2.6, ["vitamin_c"] = 14, ["potassium"] = 237, ["vitamin_a"] = 42,
            ["vitamin_k"] = 7.9
        },
        tags: new[] { "vegetable", "low calorie" },
        names: new[] { ("es", "tomate"), ("it", "pomodoro") });

        yield return App("Onion (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 40, ["protein"] = 1.1, ["total_carbohydrate"] = 9.3, ["dietary_fiber"] = 1.7,
            ["total_sugars"] = 4.2, ["vitamin_c"] = 7.4, ["folate"] = 19
        },
        tags: new[] { "vegetable", "aromatic" },
        names: new[] { ("es", "cebolla"), ("fr", "oignon") });

        yield return App("Red Bell Pepper (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 31, ["protein"] = 1, ["total_carbohydrate"] = 6, ["dietary_fiber"] = 2.1,
            ["total_sugars"] = 4.2, ["vitamin_c"] = 128, ["vitamin_a"] = 157, ["vitamin_b6"] = 0.3,
            ["vitamin_e"] = 1.6
        },
        tags: new[] { "vegetable", "low calorie" },
        aliases: new[] { "capsicum", "sweet pepper" },
        names: new[] { ("es", "pimiento rojo"), ("fr", "poivron rouge") });

        yield return App("Romaine Lettuce", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 17, ["protein"] = 1.2, ["total_carbohydrate"] = 3.3, ["dietary_fiber"] = 2.1,
            ["vitamin_a"] = 436, ["vitamin_k"] = 103, ["folate"] = 136
        },
        tags: new[] { "vegetable", "leafy green", "salad" },
        aliases: new[] { "cos lettuce" },
        names: new[] { ("es", "lechuga romana"), ("fr", "laitue romaine") });

        yield return App("Cucumber (with peel)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 15, ["protein"] = 0.7, ["total_carbohydrate"] = 3.6, ["dietary_fiber"] = 0.5,
            ["vitamin_k"] = 16, ["potassium"] = 147
        },
        tags: new[] { "vegetable", "salad", "low calorie" },
        names: new[] { ("es", "pepino"), ("fr", "concombre") });

        yield return App("Garlic (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 149, ["protein"] = 6.4, ["total_carbohydrate"] = 33, ["dietary_fiber"] = 2.1,
            ["vitamin_c"] = 31, ["calcium"] = 181, ["manganese"] = 1.7, ["selenium"] = 14.2
        },
        tags: new[] { "vegetable", "aromatic", "seasoning" },
        names: new[] { ("es", "ajo"), ("fr", "ail") });

        yield return App("White Mushrooms (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 22, ["protein"] = 3.1, ["total_carbohydrate"] = 3.3, ["dietary_fiber"] = 1,
            ["selenium"] = 9.3, ["potassium"] = 318, ["riboflavin"] = 0.4, ["niacin"] = 3.6
        },
        tags: new[] { "vegetable", "mushroom", "low calorie" },
        aliases: new[] { "button mushrooms", "champignons" },
        names: new[] { ("es", "champiñones") });

        yield return App("Green Beans (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 31, ["protein"] = 1.8, ["total_carbohydrate"] = 7, ["dietary_fiber"] = 2.7,
            ["total_sugars"] = 3.3, ["vitamin_c"] = 12.2, ["vitamin_k"] = 43
        },
        tags: new[] { "vegetable", "low calorie" },
        aliases: new[] { "string beans" },
        names: new[] { ("es", "judías verdes"), ("es", "ejotes"), ("fr", "haricots verts") });

        yield return App("Sweet Corn (cooked)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 96, ["protein"] = 3.4, ["total_carbohydrate"] = 21, ["dietary_fiber"] = 2.4,
            ["total_sugars"] = 4.5, ["folate"] = 23, ["magnesium"] = 26
        },
        tags: new[] { "vegetable", "starch" },
        aliases: new[] { "corn on the cob" },
        names: new[] { ("es", "maíz dulce"), ("fr", "maïs") });

        yield return App("Celery (raw)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 16, ["protein"] = 0.7, ["total_carbohydrate"] = 3, ["dietary_fiber"] = 1.6,
            ["sodium"] = 80, ["vitamin_k"] = 29.3, ["potassium"] = 260
        },
        tags: new[] { "vegetable", "low calorie", "snack" },
        names: new[] { ("es", "apio"), ("fr", "céleri") });

        yield return App("Avocado", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 160, ["protein"] = 2, ["total_carbohydrate"] = 8.5, ["dietary_fiber"] = 6.7,
            ["total_fat"] = 15, ["saturated_fat"] = 2.1, ["monounsaturated_fat"] = 9.8,
            ["potassium"] = 485, ["vitamin_k"] = 21, ["folate"] = 81
        },
        tags: new[] { "fruit", "healthy fat" },
        names: new[] { ("es", "aguacate"), ("fr", "avocat") });

        // ---- Fruits (per 100 g, raw) ----
        yield return App("Apple (with skin)", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 52, ["protein"] = 0.3, ["total_carbohydrate"] = 14, ["dietary_fiber"] = 2.4,
            ["total_sugars"] = 10, ["vitamin_c"] = 4.6, ["potassium"] = 107
        },
        tags: new[] { "fruit", "snack" },
        names: new[] { ("es", "manzana"), ("fr", "pomme"), ("de", "Apfel") });

        yield return App("Orange", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 47, ["protein"] = 0.9, ["total_carbohydrate"] = 12, ["dietary_fiber"] = 2.4,
            ["total_sugars"] = 9.4, ["vitamin_c"] = 53, ["calcium"] = 40, ["folate"] = 30
        },
        tags: new[] { "fruit", "citrus", "snack" },
        names: new[] { ("es", "naranja") });

        yield return App("Strawberries", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 32, ["protein"] = 0.7, ["total_carbohydrate"] = 7.7, ["dietary_fiber"] = 2,
            ["total_sugars"] = 4.9, ["vitamin_c"] = 58.8, ["folate"] = 24, ["manganese"] = 0.4
        },
        tags: new[] { "fruit", "berry" },
        names: new[] { ("es", "fresas"), ("fr", "fraises") });

        yield return App("Blueberries", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 57, ["protein"] = 0.7, ["total_carbohydrate"] = 14, ["dietary_fiber"] = 2.4,
            ["total_sugars"] = 10, ["vitamin_c"] = 9.7, ["vitamin_k"] = 19.3, ["manganese"] = 0.3
        },
        tags: new[] { "fruit", "berry" },
        names: new[] { ("es", "arándanos"), ("fr", "myrtilles") });

        yield return App("Grapes", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 69, ["protein"] = 0.7, ["total_carbohydrate"] = 18, ["dietary_fiber"] = 0.9,
            ["total_sugars"] = 15.5, ["vitamin_k"] = 14.6, ["potassium"] = 191
        },
        tags: new[] { "fruit", "snack" },
        names: new[] { ("es", "uvas"), ("fr", "raisins") });

        yield return App("Lemon", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 29, ["protein"] = 1.1, ["total_carbohydrate"] = 9.3, ["dietary_fiber"] = 2.8,
            ["total_sugars"] = 2.5, ["vitamin_c"] = 53, ["potassium"] = 138
        },
        tags: new[] { "fruit", "citrus" },
        names: new[] { ("es", "limón"), ("fr", "citron") });

        yield return App("Pineapple", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 50, ["protein"] = 0.5, ["total_carbohydrate"] = 13, ["dietary_fiber"] = 1.4,
            ["total_sugars"] = 9.9, ["vitamin_c"] = 47.8, ["manganese"] = 0.9
        },
        tags: new[] { "fruit", "tropical" },
        names: new[] { ("es", "piña"), ("fr", "ananas") });

        yield return App("Watermelon", UnitOfMeasure.Gram, 100, new()
        {
            ["calories"] = 30, ["protein"] = 0.6, ["total_carbohydrate"] = 7.6, ["dietary_fiber"] = 0.4,
            ["total_sugars"] = 6.2, ["vitamin_c"] = 8.1, ["vitamin_a"] = 28, ["potassium"] = 112
        },
        tags: new[] { "fruit", "low calorie" },
        names: new[] { ("es", "sandía"), ("fr", "pastèque") });

        // ---- Fats, condiments, sweeteners & beverages (per natural serving) ----
        yield return App("Canola Oil", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 124, ["total_fat"] = 14, ["saturated_fat"] = 1, ["monounsaturated_fat"] = 8.9,
            ["polyunsaturated_fat"] = 3.9, ["vitamin_e"] = 2.4, ["vitamin_k"] = 10
        },
        tags: new[] { "fat", "oil" },
        aliases: new[] { "rapeseed oil" },
        names: new[] { ("fr", "huile de colza") });

        yield return App("Mayonnaise", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 94, ["total_fat"] = 10, ["saturated_fat"] = 1.6, ["cholesterol"] = 6,
            ["sodium"] = 88
        },
        tags: new[] { "condiment", "fat" },
        aliases: new[] { "mayo" },
        names: new[] { ("es", "mayonesa") });

        yield return App("Ketchup", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 17, ["total_carbohydrate"] = 4.5, ["total_sugars"] = 3.7, ["sodium"] = 136
        },
        tags: new[] { "condiment", "sauce" },
        aliases: new[] { "tomato ketchup", "catsup" });

        yield return App("Soy Sauce", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 8, ["protein"] = 1.3, ["total_carbohydrate"] = 0.8, ["sodium"] = 879
        },
        tags: new[] { "condiment", "sauce" },
        aliases: new[] { "shoyu" },
        names: new[] { ("es", "salsa de soja"), ("ja", "醤油"), ("zh", "酱油") });

        yield return App("Table Salt", UnitOfMeasure.Teaspoon, 1, new()
        {
            ["calories"] = 0, ["sodium"] = 2325, ["chloride"] = 3585
        },
        tags: new[] { "seasoning", "condiment" },
        names: new[] { ("es", "sal"), ("fr", "sel") });

        yield return App("Granulated Sugar", UnitOfMeasure.Teaspoon, 1, new()
        {
            ["calories"] = 16, ["total_carbohydrate"] = 4.2, ["total_sugars"] = 4.2
        },
        tags: new[] { "sweetener", "baking" },
        aliases: new[] { "white sugar" },
        names: new[] { ("es", "azúcar"), ("fr", "sucre") });

        yield return App("Honey", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 64, ["total_carbohydrate"] = 17, ["total_sugars"] = 17
        },
        tags: new[] { "sweetener" },
        names: new[] { ("es", "miel"), ("de", "Honig") });

        yield return App("Maple Syrup", UnitOfMeasure.Tablespoon, 1, new()
        {
            ["calories"] = 52, ["total_carbohydrate"] = 13, ["total_sugars"] = 12, ["manganese"] = 0.6,
            ["calcium"] = 20, ["riboflavin"] = 0.25
        },
        tags: new[] { "sweetener", "breakfast" },
        names: new[] { ("fr", "sirop d'érable") });

        yield return App("Orange Juice", UnitOfMeasure.Cup, 1, new()
        {
            ["calories"] = 112, ["protein"] = 1.7, ["total_carbohydrate"] = 26, ["total_sugars"] = 21,
            ["vitamin_c"] = 124, ["potassium"] = 496, ["folate"] = 74, ["thiamin"] = 0.2
        },
        tags: new[] { "beverage", "fruit", "breakfast" },
        aliases: new[] { "OJ" },
        names: new[] { ("es", "jugo de naranja"), ("es", "zumo de naranja"), ("fr", "jus d'orange") });

        yield return App("Coffee, brewed (black)", UnitOfMeasure.Cup, 1, new()
        {
            ["calories"] = 2, ["sodium"] = 5, ["potassium"] = 116, ["caffeine"] = 95, ["magnesium"] = 7
        },
        tags: new[] { "beverage", "caffeine", "breakfast" },
        aliases: new[] { "black coffee" },
        names: new[] { ("es", "café") });
    }

    private static Ingredient App(string name, UnitOfMeasure unit, double serving,
        Dictionary<string, double> nutrients,
        string[]? tags = null, string[]? aliases = null, (string Lang, string Name)[]? names = null) => new()
    {
        Name = name,
        Unit = unit,
        ServingSize = serving,
        Scope = OwnershipScope.App,
        OwnerUserId = null,
        NutritionPerServing = nutrients
            .Where(kv => NutrientCatalog.IsKnown(kv.Key))
            .Select(kv => new NutrientAmount(kv.Key, kv.Value))
            .ToList(),
        Tags = tags?.ToList(),
        Aliases = aliases?.ToList(),
        LocalizedNames = names?.Select(n => new LocalizedName(n.Lang, n.Name)).ToList(),
    };
}
