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

    /// <summary>
    /// Maps the <see cref="SharePermission"/> flags enum to/from a comma-joined string of its set flag
    /// names. The Cosmos provider can't store a flags enum natively, and a string round-trips readably in
    /// the document. Permission membership is only evaluated after the invitation is materialized, so no
    /// server-side query depends on this column's shape.
    /// </summary>
    private static readonly ValueConverter<SharePermission, string> SharePermissionsConverter = new(
        v => v.ToString(),
        v => string.IsNullOrEmpty(v) ? SharePermission.None : Enum.Parse<SharePermission>(v));

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<WeightEntry> WeightEntries => Set<WeightEntry>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<ConsumptionEntry> ConsumptionEntries => Set<ConsumptionEntry>();
    public DbSet<BurnActivity> BurnActivities => Set<BurnActivity>();
    public DbSet<UserStats> UserStats => Set<UserStats>();
    public DbSet<ShareInvitation> ShareInvitations => Set<ShareInvitation>();
    public DbSet<OffContributionQueueItem> OffContributionQueue => Set<OffContributionQueueItem>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

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
            // Persist the onboarding enums by their string name (stable across reordering, readable in
            // the document) rather than their ordinal. PrimaryGoal already round-trips as a scalar enum;
            // mapping SexAtBirth the same way keeps the stored shape consistent.
            e.Property(u => u.PrimaryGoal).HasConversion<string?>();
            e.Property(u => u.SexAtBirth).HasConversion<string?>();
            // DateOfBirth is a plain nullable DateOnly both providers map natively (like the weight-schedule
            // start date) — no converter needed. Age for the Kindle BMR calc is derived from it at read time.
            // Weight-measurement schedule: a nullable enum + int + DateOnly. Persist the cadence by its
            // string name (consistent with the other onboarding enums); the interval and start date are
            // plain nullable scalars both providers map natively.
            e.Property(u => u.WeightScheduleCadence).HasConversion<string?>();
            // Persist the units preference (weight/height display) by its string name, like the other
            // onboarding enums. Nullable so user documents written before this field existed (which have no
            // UnitsPreference property) materialize as null instead of throwing; callers fall back to
            // UsImperial. A non-nullable conversion here threw "Nullable object must have a value" on read.
            e.Property(u => u.UnitsPreference).HasConversion<string?>();
        });

        // --- Ingredients ---
        b.Entity<Ingredient>(e =>
        {
            e.ToContainer("Ingredients");
            e.HasPartitionKey(i => i.Id);
            e.HasKey(i => i.Id);
            e.OwnsMany(i => i.NutritionPerServing);
            // Localized (non-English) names serialize into the document like the nutrition list.
            // Tags/Aliases are primitive string collections both providers map natively. All three
            // are nullable so ingredient documents written before the fields existed read back as
            // null instead of throwing (see the UnitsPreference note on AppUser above).
            e.OwnsMany(i => i.LocalizedNames);
        });

        // --- Recipes ---
        b.Entity<Recipe>(e =>
        {
            e.ToContainer("Recipes");
            e.HasPartitionKey(r => r.Id);
            e.HasKey(r => r.Id);
            e.OwnsMany(r => r.Items);
            // Persist the fuel type by its string name (stable across reordering, readable in the
            // document), like Meal.FuelType. Nullable so recipe documents written before this field
            // existed materialize as null instead of throwing on read; the DTO mapping resolves a null
            // read to Food.
            e.Property(r => r.FuelType).HasConversion<string?>();
        });

        // --- Meals ---
        b.Entity<Meal>(e =>
        {
            e.ToContainer("Meals");
            e.HasPartitionKey(m => m.Id);
            e.HasKey(m => m.Id);
            e.OwnsMany(m => m.Portions);
            // Persist the fuel type by its string name (stable across reordering, readable in the
            // document), like the onboarding enums. Nullable so meal documents written before this
            // field existed materialize as null instead of throwing on read; the DTO mapping resolves
            // a null read to Food. (Servings is a plain nullable scalar both providers map natively.)
            e.Property(m => m.FuelType).HasConversion<string?>();
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

        // --- Burn Activities ---
        b.Entity<BurnActivity>(e =>
        {
            e.ToContainer("BurnActivities");
            e.HasPartitionKey(a => a.Id);
            e.HasKey(a => a.Id);
        });

        // --- User Stats (gamification) ---
        b.Entity<UserStats>(e =>
        {
            e.ToContainer("UserStats");
            e.HasPartitionKey(s => s.Id);
            e.HasKey(s => s.Id);
            // The rolling daily-award ledger and completed reviews serialize into the document.
            e.OwnsMany(s => s.DailyAwards);
            e.OwnsMany(s => s.WeeklyReviews);
        });

        // --- Share Invitations (account sharing) ---
        b.Entity<ShareInvitation>(e =>
        {
            e.ToContainer("ShareInvitations");
            e.HasPartitionKey(s => s.Id);
            e.HasKey(s => s.Id);
            // Store the permissions flags enum as its string name (Cosmos can't map a flags enum), and the
            // status enum by its string name (stable across reordering, readable in the document).
            e.Property(s => s.Permissions).HasConversion(SharePermissionsConverter);
            e.Property(s => s.Status).HasConversion<string>();
        });

        // --- Open Food Facts contribution queue ---
        b.Entity<OffContributionQueueItem>(e =>
        {
            e.ToContainer("OffContributionQueue");
            e.HasPartitionKey(q => q.Id);
            e.HasKey(q => q.Id);
        });

        // --- App settings (runtime key/value store) ---
        b.Entity<AppSetting>(e =>
        {
            e.ToContainer("AppSettings");
            e.HasPartitionKey(s => s.Id);
            e.HasKey(s => s.Id);
        });

        base.OnModelCreating(b);
    }
}
