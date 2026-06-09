using System.Text.Json;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services.Nutrition;
using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Tests;

/// <summary>
/// Exercises the USDA → app-catalog mapping and the lookup service's failure handling. The external
/// HTTP call is faked (no network), and parsing is verified against a realistic FoodData Central
/// search response payload.
/// </summary>
public class NutritionLookupServiceTests
{
    /// <summary>A faked client that returns a preset response (or null to simulate an outage).</summary>
    private sealed class FakeClient : IFdcNutritionClient
    {
        private readonly FdcSearchResponse? _response;
        public string? LastQuery { get; private set; }
        public FakeClient(FdcSearchResponse? response) => _response = response;
        public Task<FdcSearchResponse?> SearchAsync(string query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(_response);
        }
    }

    // A trimmed but realistic /foods/search payload: banana, with mixed units and an untracked nutrient.
    private const string BananaJson = """
    {
      "foods": [
        {
          "description": "Bananas, raw",
          "dataType": "Foundation",
          "foodNutrients": [
            { "nutrientNumber": "208", "nutrientName": "Energy", "unitName": "KCAL", "value": 89 },
            { "nutrientNumber": "203", "nutrientName": "Protein", "unitName": "G", "value": 1.09 },
            { "nutrientNumber": "204", "nutrientName": "Total lipid (fat)", "unitName": "G", "value": 0.33 },
            { "nutrientNumber": "205", "nutrientName": "Carbohydrate", "unitName": "G", "value": 22.8 },
            { "nutrientNumber": "291", "nutrientName": "Fiber", "unitName": "G", "value": 2.6 },
            { "nutrientNumber": "269", "nutrientName": "Sugars", "unitName": "G", "value": 12.2 },
            { "nutrientNumber": "307", "nutrientName": "Sodium", "unitName": "MG", "value": 1 },
            { "nutrientNumber": "306", "nutrientName": "Potassium", "unitName": "MG", "value": 358 },
            { "nutrientNumber": "401", "nutrientName": "Vitamin C", "unitName": "MG", "value": 8.7 },
            { "nutrientNumber": "320", "nutrientName": "Vitamin A, RAE", "unitName": "UG", "value": 3 },
            { "nutrientNumber": "999", "nutrientName": "Some untracked nutrient", "unitName": "G", "value": 5 }
          ]
        }
      ]
    }
    """;

    private static FdcSearchResponse Parse(string json) =>
        JsonSerializer.Deserialize<FdcSearchResponse>(json, HttpResults.Json)!;

    private static double ValueOf(IEnumerable<NutrientAmount> amounts, string id) =>
        amounts.Single(a => a.NutrientId == id).Value;

    [Fact]
    public async Task Lookup_maps_known_nutrients_and_ignores_untracked()
    {
        var svc = new NutritionLookupService(new FakeClient(Parse(BananaJson)));

        var result = await svc.LookupAsync("banana");

        Assert.Equal("Bananas, raw", result.MatchedName);
        Assert.Equal(NutritionLookupService.SourceName, result.Source);
        Assert.Equal(100, result.BasisGrams);

        Assert.Equal(89, ValueOf(result.NutritionPer100g, "calories"));
        Assert.Equal(1.09, ValueOf(result.NutritionPer100g, "protein"), 3);
        Assert.Equal(22.8, ValueOf(result.NutritionPer100g, "total_carbohydrate"), 3);
        Assert.Equal(358, ValueOf(result.NutritionPer100g, "potassium"));
        Assert.Equal(8.7, ValueOf(result.NutritionPer100g, "vitamin_c"), 3);

        // The "999" nutrient is not in the catalog → dropped.
        Assert.DoesNotContain(result.NutritionPer100g, a => a.NutrientId == "999");
    }

    [Fact]
    public async Task Lookup_converts_units_to_catalog_basis()
    {
        // Vitamin A is stored in micrograms; USDA reports it in UG → 1:1 (3 µg).
        // Sodium catalog unit is mg; USDA reports mg → 1:1 (1 mg).
        var svc = new NutritionLookupService(new FakeClient(Parse(BananaJson)));

        var result = await svc.LookupAsync("banana");

        Assert.Equal(3, ValueOf(result.NutritionPer100g, "vitamin_a"));
        Assert.Equal(1, ValueOf(result.NutritionPer100g, "sodium"));
    }

    [Fact]
    public void Map_converts_grams_to_milligrams_for_mg_nutrient()
    {
        // Cholesterol catalog unit is mg; feed a value reported in G to verify the ×1000 conversion.
        var food = new FdcFood
        {
            Description = "Test",
            FoodNutrients = new()
            {
                new() { NutrientNumber = "601", UnitName = "G", Value = 0.05 }, // 0.05 g = 50 mg
            },
        };

        var amounts = NutritionLookupService.MapNutrients(food);

        Assert.Equal(50, ValueOf(amounts, "cholesterol"), 6);
    }

    [Fact]
    public void Map_preserves_catalog_ordering()
    {
        var amounts = NutritionLookupService.MapNutrients(Parse(BananaJson).Foods[0]);
        var ids = amounts.Select(a => a.NutrientId).ToList();

        // Calories precedes protein in the catalog, which precedes vitamin_c.
        Assert.True(ids.IndexOf("calories") < ids.IndexOf("protein"));
        Assert.True(ids.IndexOf("protein") < ids.IndexOf("vitamin_c"));
    }

    [Fact]
    public void Map_drops_unparseable_units_rather_than_misstoring()
    {
        var food = new FdcFood
        {
            FoodNutrients = new()
            {
                new() { NutrientNumber = "203", UnitName = "BOGUS", Value = 10 },
                new() { NutrientNumber = "208", UnitName = "KCAL", Value = 50 },
            },
        };

        var amounts = NutritionLookupService.MapNutrients(food);

        Assert.DoesNotContain(amounts, a => a.NutrientId == "protein"); // bad unit → dropped
        Assert.Equal(50, ValueOf(amounts, "calories"));
    }

    [Fact]
    public void Map_keeps_first_reading_when_a_nutrient_appears_twice()
    {
        var food = new FdcFood
        {
            FoodNutrients = new()
            {
                new() { NutrientNumber = "203", UnitName = "G", Value = 1.5 },
                new() { NutrientNumber = "203", UnitName = "G", Value = 9.9 },
            },
        };

        var amounts = NutritionLookupService.MapNutrients(food);

        Assert.Equal(1.5, ValueOf(amounts, "protein"), 3);
    }

    [Fact]
    public void Map_drops_negative_values()
    {
        var food = new FdcFood
        {
            FoodNutrients = new() { new() { NutrientNumber = "203", UnitName = "G", Value = -2 } },
        };

        Assert.Empty(NutritionLookupService.MapNutrients(food));
    }

    [Fact]
    public async Task Lookup_with_blank_query_throws_validation()
    {
        var svc = new NutritionLookupService(new FakeClient(Parse(BananaJson)));

        await Assert.ThrowsAsync<ValidationException>(() => svc.LookupAsync("   "));
        await Assert.ThrowsAsync<ValidationException>(() => svc.LookupAsync(null));
    }

    [Fact]
    public async Task Lookup_when_source_unavailable_throws_notfound_with_friendly_message()
    {
        // Null response simulates a network error / rate-limit (the client swallows it to null).
        var svc = new NutritionLookupService(new FakeClient(null));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => svc.LookupAsync("banana"));
        Assert.Contains("manually", ex.Message);
    }

    [Fact]
    public async Task Lookup_with_no_results_throws_notfound()
    {
        var empty = new FdcSearchResponse { Foods = new() };
        var svc = new NutritionLookupService(new FakeClient(empty));

        await Assert.ThrowsAsync<NotFoundException>(() => svc.LookupAsync("xyzzy-not-a-food"));
    }

    [Fact]
    public async Task Lookup_skips_foods_with_no_nutrients_and_uses_first_usable_match()
    {
        var response = new FdcSearchResponse
        {
            Foods = new()
            {
                new() { Description = "Empty match", FoodNutrients = new() },
                Parse(BananaJson).Foods[0],
            },
        };
        var svc = new NutritionLookupService(new FakeClient(response));

        var result = await svc.LookupAsync("banana");

        Assert.Equal("Bananas, raw", result.MatchedName);
    }

    [Fact]
    public async Task Lookup_query_is_trimmed_before_calling_the_client()
    {
        var fake = new FakeClient(Parse(BananaJson));
        var svc = new NutritionLookupService(fake);

        await svc.LookupAsync("  banana  ");

        Assert.Equal("banana", fake.LastQuery);
    }
}
