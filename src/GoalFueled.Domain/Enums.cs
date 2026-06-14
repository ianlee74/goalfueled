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

/// <summary>Biological sex at birth, captured on first login for nutrition/health calculations.</summary>
public enum SexAtBirth
{
    Male = 0,
    Female = 1
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

/// <summary>
/// Lifecycle of a user-created ingredient with respect to public visibility. Mirrors
/// <see cref="RecipeVisibility"/>: an ingredient submitted for review must be approved by an admin
/// before becoming public, and once public it can never be made private again.
/// </summary>
public enum IngredientVisibility
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

/// <summary>
/// The user's preferred unit system for displaying and entering their <b>body weight and height</b>
/// (only those). Metric uses kilograms and centimeters; US/Imperial uses pounds and feet/inches. This
/// preference does NOT affect recipe/ingredient measurement units (see <see cref="UnitOfMeasure"/>).
/// </summary>
public enum UnitsPreference
{
    /// <summary>Weight in kilograms (kg), height in centimeters (cm).</summary>
    Metric = 0,

    /// <summary>Weight in pounds (lb), height in feet/inches (ft/in).</summary>
    UsImperial = 1
}

/// <summary>
/// How often a user wants to be reminded to log a weight measurement. The anchor day-of-week (and, for
/// the "every N" cadences, the recurrence anchor) is taken from the schedule's start date.
/// </summary>
public enum WeightScheduleCadence
{
    /// <summary>A measurement is due every day.</summary>
    Daily = 0,

    /// <summary>A measurement is due once a week, on the start date's day-of-week.</summary>
    Weekly = 1,

    /// <summary>A measurement is due on the start date and every Nth day thereafter.</summary>
    EveryNDays = 2,

    /// <summary>A measurement is due on the start date's day-of-week, every Nth week from the start.</summary>
    EveryNWeeks = 3
}

/// <summary>What a logged consumption entry references for its nutrition.</summary>
public enum ConsumptionSource
{
    /// <summary>An existing catalog <see cref="Entities.Meal"/>; nutrition computed live from the meal.</summary>
    Meal = 0,

    /// <summary>
    /// An existing catalog <see cref="Entities.Recipe"/>; nutrition computed live from the recipe.
    /// No longer accepted for NEW log entries (recipes define a meal; they are not consumed directly),
    /// but the value must remain: entries persisted before that rule still carry it and must keep
    /// loading, displaying and computing.
    /// </summary>
    Recipe = 1,

    /// <summary>A free-form item not in the catalog; nutrition (if any) is captured inline on the entry.</summary>
    FreeForm = 2
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

/// <summary>
/// The kind of fuel a Fuel Source (internally <see cref="Entities.Meal"/>) represents. Stored as a
/// nullable enum on the entity; a null read (old documents predating this field) is interpreted as
/// <see cref="Food"/> in the DTO mapping.
/// </summary>
public enum FuelType
{
    Food = 0,
    Supplement = 1,
    Hydration = 2
}

/// <summary>The dimension a unit measures, used to validate that quantities are compatible.</summary>
public enum UnitDimension
{
    Mass = 0,
    Volume = 1,
    Count = 2
}
