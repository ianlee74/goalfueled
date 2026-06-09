using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

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

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<WeightEntry> WeightEntries => Set<WeightEntry>();
    public DbSet<Goal> Goals => Set<Goal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // --- Users ---
        b.Entity<AppUser>(e =>
        {
            e.ToContainer("Users");
            e.HasPartitionKey(u => u.Id);
            e.HasKey(u => u.Id);
            e.OwnsMany(u => u.ExternalLogins);
            // Roles is a List<enum>; the Cosmos provider serializes primitive collections directly.
            e.Property(u => u.Roles);
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

        base.OnModelCreating(b);
    }
}
