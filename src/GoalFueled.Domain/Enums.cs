namespace GoalFueled.Domain;

/// <summary>Application roles. Kept as an enum for simple, ordered authorization checks.</summary>
public enum AppRole
{
    User = 0,
    AppAdmin = 1
}

/// <summary>The health goal a user is targeting. Captured on first login.</summary>
public enum GoalType
{
    WeightLoss = 0,
    WeightManagement = 1,
    WeightGain = 2
}

/// <summary>Who owns/can see an entity. App-scoped entities are authored by admins and visible to everyone.</summary>
public enum OwnershipScope
{
    /// <summary>Created by an App Admin at the application level; visible to all users.</summary>
    App = 0,

    /// <summary>Created by a single user; visible only to that user (unless promoted via review).</summary>
    User = 1
}

/// <summary>
/// Lifecycle of a user-created recipe with respect to public visibility.
/// A recipe submitted for review must be approved by an admin before becoming public,
/// and once public it can never be made private again.
/// </summary>
public enum RecipeVisibility
{
    Private = 0,
    PendingReview = 1,
    Public = 2,
    Rejected = 3
}

/// <summary>Cadence of a meal plan.</summary>
public enum MealPlanCadence
{
    Daily = 0,
    Weekly = 1
}

/// <summary>The unit a recorded weight measurement is expressed in.</summary>
public enum WeightUnit
{
    Pound = 0,
    Kilogram = 1
}

/// <summary>The kind of metric a goal targets.</summary>
public enum GoalMetricType
{
    /// <summary>The user's body weight (tracked via WeightEntry).</summary>
    Weight = 0,

    /// <summary>A single nutrient from the catalog (calories, protein, vitamin C, …).</summary>
    Nutrient = 1
}

/// <summary>Whether a goal is achieved once by a deadline or repeats on a fixed cadence.</summary>
public enum GoalCadence
{
    /// <summary>Reach the target once by a target date (e.g. "lose 10 lbs by 12/31").</summary>
    OneTime = 0,

    /// <summary>Hit the target every N days starting from the start date (e.g. "2000 kcal every 7 days").</summary>
    Recurring = 1
}

/// <summary>
/// Units used to define a single serving of an ingredient. Grouped into mass, volume and count.
/// Conversions between compatible units live in <see cref="UnitOfMeasureInfo"/>.
/// </summary>
public enum UnitOfMeasure
{
    // Mass
    Gram = 0,
    Milligram = 1,
    Kilogram = 2,
    Ounce = 3,
    Pound = 4,

    // Volume
    Milliliter = 100,
    Liter = 101,
    Teaspoon = 102,
    Tablespoon = 103,
    FluidOunce = 104,
    Cup = 105,

    // Count
    Piece = 200,
    Serving = 201
}

/// <summary>The dimension a unit measures, used to validate that quantities are compatible.</summary>
public enum UnitDimension
{
    Mass = 0,
    Volume = 1,
    Count = 2
}
