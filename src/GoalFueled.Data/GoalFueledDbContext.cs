using GoalFueled.Domain;
using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GoalFueled.Data;

/// <summary>
/// EF Core context backing the app. Designed for the Cosmos DB provider (one database, a container
/// per aggregate) but also runs against the in-memory provider for local development when the
/// Cosmos emulator is not available. Owned collections (nutrition, recipe items, etc.) serialize
/// naturally into each document.
/// </summary>
public class GoalFueledDbContext : DbContext
{
    public GoalFueledDbContext(DbContextOptions<GoalFueledDbContext> options) : base(options) { }

    /// <summary>Maps <see cref="AppUser.Roles"/> (a list of enums, unsupported by Cosmos) to/from a comma-joined string of role names.</summary>
    private static readonly ValueConverter<List<AppRole>, string> RolesConverter = new(
        v => string.Join(',', v.Select(r => r.ToString())),
        v => string.IsNullOrEmpty(v)
            ? new List<AppRole>()
            : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<AppRole>).ToList());

    /// <summary>Lets EF track changes to the converted <see cref="AppUser.Roles"/> list correctly.</summary>
    private static readonly ValueComparer<List<AppRole>> RolesComparer = new(
        (a, b) => (a ?? new List<AppRole>()).SequenceEqual(b ?? new List<AppRole>()),
        v => v.Aggregate(0, (h, r) => HashCode.Combine(h, (int)r)),
        v => v.ToList());

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<WeightEntry> WeightEntries => Set<WeightEntry>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<ConsumptionEntry> ConsumptionEntries => Set<ConsumptionEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // --- Users ---
        b.Entity<AppUser>(e =>
        {
            e.ToContainer("Users");
            e.HasPartitionKey(u => u.Id);
            e.HasKey(u => u.Id);
            e.OwnsMany(u => u.ExternalLogins);
            // The Cosmos provider (and the relational providers) can't map a List<enum> directly, so
            // store the roles as their comma-joined string names via a value converter. Role membership
            // is only ever evaluated after the user is materialized (e.g. CountAdminsAsync), so no
            // server-side query depends on this column's shape.
            e.Property(u => u.Roles).HasConversion(RolesConverter, RolesComparer);
        });

        // --- Ingredients ---
        b.Entity<Ingredient>(e =>
        {
            e.ToContainer("Ingredients");
            e.HasPartitionKey(i => i.Id);
            e.HasKey(i => i.Id);
            e.OwnsMany(i => i.NutritionPerServing);
        });

        // --- Recipes ---
        b.Entity<Recipe>(e =>
        {
            e.ToContainer("Recipes");
            e.HasPartitionKey(r => r.Id);
            e.HasKey(r => r.Id);
            e.OwnsMany(r => r.Items);
        });

        // --- Meals ---
        b.Entity<Meal>(e =>
        {
            e.ToContainer("Meals");
            e.HasPartitionKey(m => m.Id);
            e.HasKey(m => m.Id);
            e.OwnsMany(m => m.Portions);
        });

        // --- Meal Plans ---
        b.Entity<MealPlan>(e =>
        {
            e.ToContainer("MealPlans");
            e.HasPartitionKey(p => p.Id);
            e.HasKey(p => p.Id);
            e.OwnsMany(p => p.ScheduledMeals);
            e.OwnsMany(p => p.UntimedItems);
        });

        // --- Weight Entries ---
        b.Entity<WeightEntry>(e =>
        {
            e.ToContainer("WeightEntries");
            e.HasPartitionKey(w => w.Id);
            e.HasKey(w => w.Id);
        });

        // --- Goals ---
        b.Entity<Goal>(e =>
        {
            e.ToContainer("Goals");
            e.HasPartitionKey(g => g.Id);
            e.HasKey(g => g.Id);
        });

        // --- Consumption Entries ---
        b.Entity<ConsumptionEntry>(e =>
        {
            e.ToContainer("ConsumptionEntries");
            e.HasPartitionKey(c => c.Id);
            e.HasKey(c => c.Id);
            e.OwnsMany(c => c.Nutrition);
        });

        base.OnModelCreating(b);
    }
}
