using GoalFueled.Domain.Nutrition;

namespace GoalFueled.Domain.Entities;

/// <summary>
/// A single item a user actually ate, recorded in their personal daily consumption log. This is the
/// app's model of <em>actual</em> intake (as opposed to a meal plan, which is <em>planned</em> intake).
/// Consumption is strictly per-user data: each entry is owned by, and only visible to, the user who
/// logged it (there is no app-level or cross-user view, even for admins, mirroring <see cref="WeightEntry"/>).
///
/// <para>Unlike weight there is no per-day uniqueness — a day may have many logged items (a snack here,
/// an extra coffee there), so logging always appends a new entry and removal is by id.</para>
///
/// <para>An entry references the catalog (a <see cref="Meal"/> or <see cref="Recipe"/>) so its nutrition
/// is known and computed live from the source, OR it is a free-form item that carries its own optional
/// nutrition inline. The <see cref="Source"/> discriminates the two.</para>
/// </summary>
public class ConsumptionEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The user who logged (and owns) this entry.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>The calendar day the item was consumed on.</summary>
    public DateOnly Date { get; set; }

    /// <summary>What kind of thing was logged: an existing meal, an existing recipe, or a free-form item.</summary>
    public ConsumptionSource Source { get; set; } = ConsumptionSource.FreeForm;

    /// <summary>
    /// The referenced catalog item's id when <see cref="Source"/> is <see cref="ConsumptionSource.Meal"/>
    /// or <see cref="ConsumptionSource.Recipe"/>; null for free-form entries.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// A human-readable name for the logged item. For catalog entries this is a snapshot of the
    /// meal/recipe name at log time (so the log still reads sensibly if the source is later renamed or
    /// deleted); for free-form entries it is the user-entered name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// How much was eaten, in servings of the referenced meal/recipe (e.g. 0.5 = half). For free-form
    /// entries this scales the inline <see cref="Nutrition"/> the same way (defaults to 1).
    /// </summary>
    public double Servings { get; set; } = 1;

    /// <summary>
    /// Inline nutrition for a free-form entry, expressed per single serving (scaled by <see cref="Servings"/>
    /// when totalled). Empty/unused for catalog entries, whose nutrition is computed live from the source.
    /// </summary>
    public List<NutrientAmount> Nutrition { get; set; } = new();

    /// <summary>
    /// Set when this entry was created by <em>completing a planned-meal task</em> in The Log: a stable key
    /// (<c>planId:mealId:day:time</c>) that correlates the completion with one specific task of the active
    /// plan on this <see cref="Date"/>. Lets The Log render the task as checked and toggle it off again.
    /// Null for ad-hoc entries. It only correlates; nutrition still rolls up the normal way (no special
    /// casing), so totals never double-count.
    /// </summary>
    public string? PlannedMealRef { get; set; }

    /// <summary>
    /// True when this entry records that a planned-meal task was <em>skipped</em> rather than eaten: it
    /// carries the task's <see cref="PlannedMealRef"/> so The Log can show the task as skipped, but it
    /// contributes <em>zero</em> nutrition and is excluded from the day's logged-items list and totals
    /// (a skip must never inflate or deflate intake). Always false for real (eaten) entries.
    /// </summary>
    public bool Skipped { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
