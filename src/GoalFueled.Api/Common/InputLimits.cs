namespace GoalFueled.Api.Common;

/// <summary>
/// Shared upper bounds for user-supplied input, applied uniformly across the services' validation so
/// a single oversized payload can't bloat storage or DoS the nutrition roll-ups. Values are generous
/// for legitimate use; they exist as a safety ceiling, not a UX constraint. Throws
/// <see cref="ValidationException"/> via the small helpers below, keeping messages consistent.
/// </summary>
public static class InputLimits
{
    /// <summary>Max length for short single-line text fields (names).</summary>
    public const int NameMaxLength = 200;

    /// <summary>Max length for long free-text fields (descriptions, which may contain HTML).</summary>
    public const int DescriptionMaxLength = 10_000;

    /// <summary>Max length for a URL-bearing field (image URLs).</summary>
    public const int UrlMaxLength = 2_000;

    /// <summary>Max length for a short free-text note on an untimed plan item.</summary>
    public const int NoteMaxLength = 500;

    /// <summary>Max items in a single recipe.</summary>
    public const int RecipeItemsMax = 100;

    /// <summary>Max nutrition entries on a single ingredient (bounded by the catalog in practice).</summary>
    public const int NutritionEntriesMax = 200;

    /// <summary>Max entries in each of an ingredient's search-metadata lists (tags, aliases, localized names).</summary>
    public const int IngredientSearchTermsMax = 50;

    /// <summary>Max portions in a single meal.</summary>
    public const int MealPortionsMax = 100;

    /// <summary>Max scheduled meals in a single meal plan.</summary>
    public const int ScheduledMealsMax = 200;

    /// <summary>Max untimed items in a single meal plan.</summary>
    public const int UntimedItemsMax = 200;

    /// <summary>Upper bound for a recorded weight value (covers any plausible lb or kg figure).</summary>
    public const double WeightValueMax = 5_000;

    /// <summary>Upper bound for a profile height in centimeters (generous ceiling above any human height).</summary>
    public const double HeightCmMax = 300;

    /// <summary>Upper bound for a weight-measurement schedule's "every N" interval (days or weeks).</summary>
    public const int WeightScheduleIntervalMax = 365;

    /// <summary>Upper bound for a goal's target value (generous ceiling across weight and nutrient units).</summary>
    public const double GoalTargetValueMax = 10_000_000;

    /// <summary>Upper bound for a recurring goal's period length, in days (~5 years).</summary>
    public const int GoalRecurrenceDaysMax = 1_825;

    /// <summary>Upper bound for the servings on a single logged consumption entry.</summary>
    public const double ConsumptionServingsMax = 10_000;

    /// <summary>Upper bound for the calories burned on a single logged activity (a generous ceiling).</summary>
    public const int BurnCaloriesMax = 100_000;

    /// <summary>Max length of one message sent to the AI assistant (a spoken or typed sentence).</summary>
    public const int AssistantMessageMaxLength = 2_000;

    /// <summary>
    /// Max turns in an assistant conversation echoed back by the client. Conversation state is client-held,
    /// so this is what stops an unbounded transcript from being replayed into a (billed) model call.
    /// </summary>
    public const int AssistantConversationTurnsMax = 60;

    /// <summary>
    /// Max total characters across an echoed conversation's content blocks. A second, tighter ceiling on the
    /// same untrusted input: turn COUNT alone can't bound the token cost of one enormous turn.
    /// </summary>
    public const int AssistantConversationCharsMax = 200_000;

    /// <summary>
    /// Builds the name for a customized clone: the original with a " (custom)" suffix so it's
    /// distinguishable from the app-level source in lists. Truncates the original first if needed so
    /// the result still fits within <see cref="NameMaxLength"/>.
    /// </summary>
    public static string CustomizedName(string original)
    {
        const string suffix = " (custom)";
        var name = (original ?? string.Empty).Trim();
        var room = NameMaxLength - suffix.Length;
        if (name.Length > room) name = name[..room].TrimEnd();
        return name + suffix;
    }

    /// <summary>Validates a required string is non-empty and within <paramref name="maxLength"/>.</summary>
    public static void RequireText(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        if (value.Length > maxLength)
            throw new ValidationException($"{label} must be {maxLength} characters or fewer.");
    }

    /// <summary>Validates an optional string is within <paramref name="maxLength"/> when present.</summary>
    public static void LimitText(string? value, int maxLength, string label)
    {
        if (value is not null && value.Length > maxLength)
            throw new ValidationException($"{label} must be {maxLength} characters or fewer.");
    }

    /// <summary>Validates a collection's size does not exceed <paramref name="max"/>.</summary>
    public static void LimitCount(int count, int max, string label)
    {
        if (count > max)
            throw new ValidationException($"A maximum of {max} {label} is allowed.");
    }
}
