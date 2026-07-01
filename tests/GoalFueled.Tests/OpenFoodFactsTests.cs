using System.Text.Json;
using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Api.Services.OpenFoodFacts;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;
using GoalFueled.Domain.Nutrition;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoalFueled.Tests;

/// <summary>
/// Exercises the Open Food Facts nutriment mapping, barcode-lookup orchestration, and contribution
/// field-building. The external HTTP call is faked (no network); parsing runs against realistic payloads.
/// </summary>
public class OpenFoodFactsTests
{
    /// <summary>A faked OFF client: returns a preset product for reads and records contribution writes.</summary>
    private sealed class FakeOffClient : IOffClient
    {
        private readonly OffProduct? _product;
        public bool CanContribute { get; set; } = true;
        public bool ContributeSucceeds { get; set; } = true;
        public string? LastLookupBarcode { get; private set; }
        public string? ContributedBarcode { get; private set; }
        public int ContributeCallCount { get; private set; }
        public IReadOnlyList<KeyValuePair<string, string>>? ContributedFields { get; private set; }

        public FakeOffClient(OffProduct? product) => _product = product;

        public Task<OffProduct?> GetProductAsync(string barcode, CancellationToken ct = default)
        {
            LastLookupBarcode = barcode;
            return Task.FromResult(_product);
        }

        public Task<bool> ContributeAsync(
            string barcode, IReadOnlyList<KeyValuePair<string, string>> fields, CancellationToken ct = default)
        {
            ContributeCallCount++;
            ContributedBarcode = barcode;
            ContributedFields = fields;
            return Task.FromResult(ContributeSucceeds);
        }
    }

    /// <summary>A minimal ingredient repo supporting the barcode-lookup and by-id paths; everything else throws.</summary>
    private sealed class FakeRepo : IIngredientRepository
    {
        private readonly IReadOnlyList<Ingredient> _items;
        public FakeRepo(params Ingredient[] items) => _items = items;

        public Task<Ingredient?> GetByBarcodeVisibleAsync(string userId, string barcode, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(i => i.Barcode == barcode));

        public Task<Ingredient?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(i => i.Id == id));

        public Task<IReadOnlyList<Ingredient>> ListVisibleAsync(string userId, string? search = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Ingredient>> ListAllAsync(string? search = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Ingredient>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Ingredient>> ListPendingReviewAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountPendingReviewByOwnerAsync(string ownerUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ingredient> AddAsync(Ingredient ingredient, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Ingredient ingredient, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Ingredient ingredient, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>An in-memory OFF contribution queue.</summary>
    private sealed class FakeQueue : IOffContributionQueue
    {
        public List<OffContributionQueueItem> Items { get; } = new();

        public Task UpsertAsync(string barcode, string ingredientId, CancellationToken ct = default)
        {
            var existing = Items.FirstOrDefault(i => i.Barcode == barcode);
            if (existing is not null)
            {
                existing.IngredientId = ingredientId;
                existing.Attempts = 0;
                existing.LastError = null;
            }
            else
            {
                Items.Add(new OffContributionQueueItem { Barcode = barcode, IngredientId = ingredientId });
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OffContributionQueueItem>> ListPendingAsync(int max, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OffContributionQueueItem>>(
                Items.OrderBy(i => i.CreatedAt).Take(max).ToList());

        public Task UpdateAsync(OffContributionQueueItem item, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(OffContributionQueueItem item, CancellationToken ct = default)
        {
            Items.Remove(item);
            return Task.CompletedTask;
        }
    }

    private static OffContributionService Contribution(FakeOffClient off, params Ingredient[] ingredients)
        => new(off, new FakeQueue(), new FakeRepo(ingredients), NullLogger<OffContributionService>.Instance);

    private static Dictionary<string, JsonElement> Nutriments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, HttpResults.Json)!;

    private static double ValueOf(IEnumerable<NutrientAmount> amounts, string id) =>
        amounts.Single(a => a.NutrientId == id).Value;

    // OFF _100g values are canonical: kcal for energy, grams for everything else (incl. minerals/vitamins).
    private const string ProductNutrimentsJson = """
    {
      "energy-kcal_100g": 230,
      "proteins_100g": 9,
      "fat_100g": 3,
      "saturated-fat_100g": 0.5,
      "carbohydrates_100g": 42,
      "sugars_100g": 5,
      "fiber_100g": 6,
      "sodium_100g": 0.5,
      "calcium_100g": 0.12,
      "iron_100g": 0.0008,
      "vitamin-a_100g": 0.0000005,
      "vitamin-c_100g": 0.006,
      "made-up-nutrient_100g": 3
    }
    """;

    [Fact]
    public void Map_converts_off_canonical_units_and_ignores_untracked()
    {
        var amounts = OffNutrimentMap.MapPer100g(Nutriments(ProductNutrimentsJson));

        Assert.Equal(230, ValueOf(amounts, "calories"));
        Assert.Equal(9, ValueOf(amounts, "protein"));
        Assert.Equal(42, ValueOf(amounts, "total_carbohydrate"));
        Assert.Equal(5, ValueOf(amounts, "total_sugars"));
        Assert.Equal(6, ValueOf(amounts, "dietary_fiber"));
        // grams → catalog unit
        Assert.Equal(500, ValueOf(amounts, "sodium"), 6);       // 0.5 g → mg
        Assert.Equal(120, ValueOf(amounts, "calcium"), 6);      // 0.12 g → mg
        Assert.Equal(0.8, ValueOf(amounts, "iron"), 6);         // 0.0008 g → mg
        Assert.Equal(0.5, ValueOf(amounts, "vitamin_a"), 6);    // 0.0000005 g → µg
        Assert.Equal(6, ValueOf(amounts, "vitamin_c"), 6);      // 0.006 g → mg

        Assert.DoesNotContain(amounts, a => a.NutrientId == "made-up-nutrient");
    }

    [Fact]
    public void Map_preserves_catalog_ordering()
    {
        var ids = OffNutrimentMap.MapPer100g(Nutriments(ProductNutrimentsJson)).Select(a => a.NutrientId).ToList();
        Assert.True(ids.IndexOf("calories") < ids.IndexOf("protein"));
        Assert.True(ids.IndexOf("total_carbohydrate") < ids.IndexOf("vitamin_c"));
    }

    [Fact]
    public void Map_derives_sodium_from_salt_when_sodium_absent()
    {
        var amounts = OffNutrimentMap.MapPer100g(Nutriments("""{ "salt_100g": 1.25 }"""));
        // salt 1.25 g ÷ 2.5 = 0.5 g sodium → 500 mg
        Assert.Equal(500, ValueOf(amounts, "sodium"), 6);
    }

    [Fact]
    public void Map_accepts_numeric_strings()
    {
        var amounts = OffNutrimentMap.MapPer100g(Nutriments("""{ "proteins_100g": "9.5" }"""));
        Assert.Equal(9.5, ValueOf(amounts, "protein"), 6);
    }

    [Fact]
    public void Canonical_conversions_round_trip()
    {
        var sodium = NutrientCatalog.Find("sodium")!;
        Assert.Equal(0.5, OffNutrimentMap.ToOffCanonical(500, sodium)!.Value, 6); // 500 mg → 0.5 g
        Assert.Equal(500, OffNutrimentMap.FromOffCanonical(0.5, sodium)!.Value, 6); // 0.5 g → 500 mg
    }

    // ---- BarcodeLookupService ----

    private static OffProduct SampleProduct() => new()
    {
        ProductName = "Test Cereal",
        Brands = "Testco",
        ImageNutritionUrl = "https://images.example/nutrition.jpg",
        Nutriments = Nutriments(ProductNutrimentsJson),
    };

    [Fact]
    public async Task Lookup_maps_off_product()
    {
        var svc = new BarcodeLookupService(new FakeOffClient(SampleProduct()), new FakeRepo());

        var result = await svc.LookupAsync("user-1", "737628064502");

        Assert.True(result.Found);
        Assert.Null(result.ExistingIngredientId);
        Assert.Equal(BarcodeLookupService.OffSourceName, result.Source);
        Assert.Equal("Test Cereal", result.ProductName);
        Assert.Equal("https://images.example/nutrition.jpg", result.ImageUrl);
        Assert.Equal(230, ValueOf(result.NutritionPer100g, "calories"));
    }

    [Fact]
    public async Task Lookup_returns_existing_library_item_without_calling_off()
    {
        var mine = new Ingredient { Id = "ing-1", Name = "My Cereal", Barcode = "737628064502", Scope = OwnershipScope.User, OwnerUserId = "user-1" };
        var off = new FakeOffClient(SampleProduct());
        var svc = new BarcodeLookupService(off, new FakeRepo(mine));

        var result = await svc.LookupAsync("user-1", "737628064502");

        Assert.True(result.Found);
        Assert.Equal("ing-1", result.ExistingIngredientId);
        Assert.Equal(BarcodeLookupService.LibrarySourceName, result.Source);
        Assert.Null(off.LastLookupBarcode); // OFF was never consulted.
    }

    [Fact]
    public async Task Lookup_reports_not_found_when_off_has_nothing()
    {
        var svc = new BarcodeLookupService(new FakeOffClient(null), new FakeRepo());

        var result = await svc.LookupAsync("user-1", "000000000000");

        Assert.False(result.Found);
        Assert.Null(result.Source);
        Assert.Empty(result.NutritionPer100g);
    }

    [Fact]
    public async Task Lookup_normalizes_barcode_and_rejects_junk()
    {
        var off = new FakeOffClient(null);
        var svc = new BarcodeLookupService(off, new FakeRepo());

        await svc.LookupAsync("user-1", " 7 3 7 6 2 8 0 6 4 5 0 2 ");
        Assert.Equal("737628064502", off.LastLookupBarcode);

        await Assert.ThrowsAsync<ValidationException>(() => svc.LookupAsync("user-1", "12"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.LookupAsync("user-1", "   "));
    }

    // ---- OffContributionService.BuildFields ----

    private static Ingredient Ing(UnitOfMeasure unit, double serving, params (string Id, double Value)[] nutrients) => new()
    {
        Name = "Contributed Product",
        Barcode = "111111111111",
        Unit = unit,
        ServingSize = serving,
        // Only public ingredients are contributed; the worker enforces this at send time.
        Visibility = IngredientVisibility.Public,
        NutritionPerServing = nutrients.Select(n => new NutrientAmount(n.Id, n.Value)).ToList(),
    };

    private static string Field(IEnumerable<KeyValuePair<string, string>> fields, string key) =>
        fields.First(f => f.Key == key).Value;

    [Fact]
    public void BuildFields_scales_mass_serving_to_per_100g()
    {
        // 50 g serving with 45 kcal + 2 g protein → per-100 g doubles both.
        var fields = OffContributionService.BuildFields(Ing(UnitOfMeasure.Gram, 50, ("calories", 45), ("protein", 2)));

        Assert.Equal("100g", Field(fields, "nutrition_data_per"));
        Assert.Equal("Contributed Product", Field(fields, "product_name"));
        Assert.Equal("90", Field(fields, "nutriment_energy-kcal"));
        Assert.Equal("kcal", Field(fields, "nutriment_energy-kcal_unit"));
        Assert.Equal("4", Field(fields, "nutriment_proteins"));
        Assert.Equal("g", Field(fields, "nutriment_proteins_unit"));
    }

    [Fact]
    public void BuildFields_converts_catalog_units_to_grams()
    {
        // sodium is stored in mg; 500 mg per 100 g → 0.5 g for OFF.
        var fields = OffContributionService.BuildFields(Ing(UnitOfMeasure.Gram, 100, ("sodium", 500)));

        Assert.Equal("0.5", Field(fields, "nutriment_sodium"));
        Assert.Equal("g", Field(fields, "nutriment_sodium_unit"));
    }

    [Fact]
    public void BuildFields_uses_serving_basis_for_non_mass_units()
    {
        var fields = OffContributionService.BuildFields(Ing(UnitOfMeasure.Piece, 1, ("calories", 100)));

        Assert.Equal("serving", Field(fields, "nutrition_data_per"));
        Assert.Equal("1 pc", Field(fields, "serving_size"));
        Assert.Equal("100", Field(fields, "nutriment_energy-kcal"));
    }

    [Fact]
    public void BuildFields_is_empty_when_no_nutriments_map()
    {
        var fields = OffContributionService.BuildFields(Ing(UnitOfMeasure.Gram, 100));
        Assert.Empty(fields);
    }

    // ---- OffContributionService.ContributeIfNewAsync ----

    [Fact]
    public async Task Contribute_skips_products_off_already_has()
    {
        var off = new FakeOffClient(SampleProduct()); // OFF already knows this barcode
        var svc = Contribution(off);

        var outcome = await svc.ContributeIfNewAsync(Ing(UnitOfMeasure.Gram, 100, ("calories", 100)));

        Assert.Equal(OffContributionOutcome.AlreadyPresent, outcome);
        Assert.Null(off.ContributedBarcode); // never written
    }

    [Fact]
    public async Task Contribute_writes_products_off_is_missing()
    {
        var off = new FakeOffClient(null); // OFF doesn't have it
        var svc = Contribution(off);

        var outcome = await svc.ContributeIfNewAsync(Ing(UnitOfMeasure.Gram, 100, ("calories", 100)));

        Assert.Equal(OffContributionOutcome.Contributed, outcome);
        Assert.Equal("111111111111", off.ContributedBarcode);
        Assert.NotNull(off.ContributedFields);
        Assert.Contains(off.ContributedFields!, f => f.Key == "nutriment_energy-kcal");
    }

    [Fact]
    public async Task Contribute_is_skipped_when_no_write_account()
    {
        var off = new FakeOffClient(null) { CanContribute = false };
        var svc = Contribution(off);

        var outcome = await svc.ContributeIfNewAsync(Ing(UnitOfMeasure.Gram, 100, ("calories", 100)));

        Assert.Equal(OffContributionOutcome.Disabled, outcome);
        Assert.Null(off.LastLookupBarcode);  // didn't even check
        Assert.Null(off.ContributedBarcode);
    }

    // ---- EnqueueIfEligibleAsync (called on save) ----

    [Fact]
    public async Task Enqueue_queues_a_barcoded_product()
    {
        var off = new FakeOffClient(null);
        var queue = new FakeQueue();
        var svc = new OffContributionService(off, queue, new FakeRepo(), NullLogger<OffContributionService>.Instance);

        await svc.EnqueueIfEligibleAsync(Ing(UnitOfMeasure.Gram, 100, ("calories", 100)));

        Assert.Single(queue.Items);
        Assert.Equal("111111111111", queue.Items[0].Barcode);
        Assert.Null(off.LastLookupBarcode); // enqueue makes no OFF call
    }

    [Fact]
    public async Task Enqueue_skips_when_no_barcode_or_no_account()
    {
        var noBarcode = new Ingredient { Name = "x", Unit = UnitOfMeasure.Gram, ServingSize = 100 };
        var queue = new FakeQueue();
        var svc = new OffContributionService(new FakeOffClient(null), queue, new FakeRepo(), NullLogger<OffContributionService>.Instance);
        await svc.EnqueueIfEligibleAsync(noBarcode);
        Assert.Empty(queue.Items);

        var queue2 = new FakeQueue();
        var disabled = new OffContributionService(new FakeOffClient(null) { CanContribute = false }, queue2, new FakeRepo(), NullLogger<OffContributionService>.Instance);
        await disabled.EnqueueIfEligibleAsync(Ing(UnitOfMeasure.Gram, 100, ("calories", 100)));
        Assert.Empty(queue2.Items);
    }

    // ---- ProcessQueueAsync (the timer worker's drain) ----

    private static OffContributionService WithQueue(FakeOffClient off, FakeQueue queue, params Ingredient[] ingredients)
        => new(off, queue, new FakeRepo(ingredients), NullLogger<OffContributionService>.Instance);

    [Fact]
    public async Task Process_contributes_missing_products_and_clears_them()
    {
        var ing = Ing(UnitOfMeasure.Gram, 100, ("calories", 100));
        var off = new FakeOffClient(null); // missing on OFF
        var queue = new FakeQueue();
        queue.Items.Add(new OffContributionQueueItem { Barcode = ing.Barcode!, IngredientId = ing.Id });
        var svc = WithQueue(off, queue, ing);

        var sent = await svc.ProcessQueueAsync(batchSize: 10, delay: TimeSpan.Zero);

        Assert.Equal(1, sent);
        Assert.Equal(ing.Barcode, off.ContributedBarcode);
        Assert.Empty(queue.Items); // removed once contributed
    }

    [Fact]
    public async Task Process_drops_items_off_already_has_without_writing()
    {
        var ing = Ing(UnitOfMeasure.Gram, 100, ("calories", 100));
        var off = new FakeOffClient(SampleProduct()); // OFF already has it
        var queue = new FakeQueue();
        queue.Items.Add(new OffContributionQueueItem { Barcode = ing.Barcode!, IngredientId = ing.Id });
        var svc = WithQueue(off, queue, ing);

        var sent = await svc.ProcessQueueAsync(batchSize: 10, delay: TimeSpan.Zero);

        Assert.Equal(0, sent);
        Assert.Null(off.ContributedBarcode);
        Assert.Empty(queue.Items); // still cleared — nothing to do
    }

    [Fact]
    public async Task Process_drops_items_whose_ingredient_is_gone()
    {
        var off = new FakeOffClient(null);
        var queue = new FakeQueue();
        queue.Items.Add(new OffContributionQueueItem { Barcode = "111111111111", IngredientId = "missing" });
        var svc = WithQueue(off, queue); // repo has no ingredients

        var sent = await svc.ProcessQueueAsync(batchSize: 10, delay: TimeSpan.Zero);

        Assert.Equal(0, sent);
        Assert.Null(off.LastLookupBarcode); // never even checked OFF
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task Process_retries_failures_then_gives_up_at_the_cap()
    {
        var ing = Ing(UnitOfMeasure.Gram, 100, ("calories", 100));
        var off = new FakeOffClient(null) { ContributeSucceeds = false }; // write keeps failing
        var queue = new FakeQueue();
        var item = new OffContributionQueueItem { Barcode = ing.Barcode!, IngredientId = ing.Id };
        queue.Items.Add(item);
        var svc = WithQueue(off, queue, ing);

        // Each run attempts once and increments Attempts; the row survives until the cap, then is dropped.
        for (var run = 1; run < OffContributionService.MaxAttempts; run++)
        {
            var sent = await svc.ProcessQueueAsync(batchSize: 10, delay: TimeSpan.Zero);
            Assert.Equal(0, sent);
            Assert.Single(queue.Items);
            Assert.Equal(run, queue.Items[0].Attempts);
        }

        await svc.ProcessQueueAsync(batchSize: 10, delay: TimeSpan.Zero); // the MaxAttempts-th failure
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task Process_respects_batch_size()
    {
        var off = new FakeOffClient(null);
        var queue = new FakeQueue();
        var ingredients = new List<Ingredient>();
        for (var i = 0; i < 5; i++)
        {
            var ing = new Ingredient { Name = $"p{i}", Barcode = $"1000000000{i:D2}", Unit = UnitOfMeasure.Gram, ServingSize = 100, Visibility = IngredientVisibility.Public, NutritionPerServing = { new NutrientAmount("calories", 100) } };
            ingredients.Add(ing);
            queue.Items.Add(new OffContributionQueueItem { Barcode = ing.Barcode!, IngredientId = ing.Id });
        }
        var svc = WithQueue(off, queue, ingredients.ToArray());

        var sent = await svc.ProcessQueueAsync(batchSize: 2, delay: TimeSpan.Zero);

        Assert.Equal(2, sent);
        Assert.Equal(2, off.ContributeCallCount);
        Assert.Equal(3, queue.Items.Count); // the remaining 3 wait for the next run
    }

    [Fact]
    public async Task Process_does_nothing_when_disabled()
    {
        var off = new FakeOffClient(null) { CanContribute = false };
        var queue = new FakeQueue();
        queue.Items.Add(new OffContributionQueueItem { Barcode = "111111111111", IngredientId = "x" });
        var svc = WithQueue(off, queue);

        var sent = await svc.ProcessQueueAsync(batchSize: 10, delay: TimeSpan.Zero);

        Assert.Equal(0, sent);
        Assert.Single(queue.Items); // queue left intact for when an account is configured
    }

    [Fact]
    public async Task Process_drops_non_public_ingredients_without_contributing()
    {
        // A queued item whose ingredient is not public (e.g. approval was reverted) must never be sent.
        var priv = new Ingredient
        {
            Name = "Private", Barcode = "111111111111", Unit = UnitOfMeasure.Gram, ServingSize = 100,
            NutritionPerServing = { new NutrientAmount("calories", 100) },
            // Visibility null + user scope ⇒ EffectiveVisibility Private.
        };
        var off = new FakeOffClient(null);
        var queue = new FakeQueue();
        queue.Items.Add(new OffContributionQueueItem { Barcode = priv.Barcode!, IngredientId = priv.Id });
        var svc = WithQueue(off, queue, priv);

        var sent = await svc.ProcessQueueAsync(batchSize: 10, delay: TimeSpan.Zero);

        Assert.Equal(0, sent);
        Assert.Null(off.LastLookupBarcode); // never even checked OFF
        Assert.Empty(queue.Items);          // dropped
    }

    // ---- IngredientService gating: only public ingredients are queued ----

    private static async Task<AppUser> UserAsync(GoalFueledDbContext db, string subject) =>
        (await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", subject, $"{subject}@dev.local", subject))
            .GetCurrentUserAsync(TestHelpers.BlankRequest()))!;

    private static IngredientInput BarcodedInput(bool appLevel = false, string barcode = "999888777666") =>
        new("Scanned Bar", null, null, UnitOfMeasure.Gram, 100,
            new[] { new NutrientAmount("calories", 100) }, AppLevel: appLevel, Barcode: barcode);

    [Fact]
    public async Task Private_ingredient_is_queued_only_after_it_passes_review()
    {
        using var db = TestHelpers.NewContext();
        var admin = await UserAsync(db, "admin");
        var user = await UserAsync(db, "user");

        var queue = new FakeQueue();
        var contribution = new OffContributionService(
            new FakeOffClient(null), queue, new IngredientRepository(db), NullLogger<OffContributionService>.Instance);
        var svc = new IngredientService(new IngredientRepository(db), contribution);

        var created = await svc.CreateAsync(user, BarcodedInput(), default);
        Assert.Empty(queue.Items); // private on create → not queued

        await svc.SubmitForReviewAsync(user, created.Id, default);
        Assert.Empty(queue.Items); // pending → still not queued

        await svc.ApproveAsync(admin, created.Id, default);
        Assert.Single(queue.Items); // approved (public) → queued
        Assert.Equal("999888777666", queue.Items[0].Barcode);
    }

    [Fact]
    public async Task Admin_app_level_barcoded_ingredient_is_queued_on_create()
    {
        using var db = TestHelpers.NewContext();
        var admin = await UserAsync(db, "admin");

        var queue = new FakeQueue();
        var contribution = new OffContributionService(
            new FakeOffClient(null), queue, new IngredientRepository(db), NullLogger<OffContributionService>.Instance);
        var svc = new IngredientService(new IngredientRepository(db), contribution);

        await svc.CreateAsync(admin, BarcodedInput(appLevel: true, barcode: "555555555555"), default);

        Assert.Single(queue.Items); // app-level is public immediately → queued
        Assert.Equal("555555555555", queue.Items[0].Barcode);
    }
}
