namespace GoalFueled.Domain.Entities;

/// <summary>
/// A persisted user profile, created on first authenticated request. Identity providers
/// (Google/Microsoft/Facebook, or the local dev provider) are linked via <see cref="ExternalLogins"/>
/// so a single profile can be reached through more than one provider.
/// </summary>
public class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    /// <summary>Linked identity-provider subjects. A user may sign in via several providers.</summary>
    public List<ExternalLogin> ExternalLogins { get; set; } = new();

    /// <summary>
    /// Flat "provider:subject" keys mirroring <see cref="ExternalLogins"/>. Maintained by
    /// <see cref="LinkLogin"/> so lookups translate to a single Cosmos ARRAY_CONTAINS query.
    /// </summary>
    public List<string> LoginKeys { get; set; } = new();

    public List<AppRole> Roles { get; set; } = new() { AppRole.User };

    /// <summary>
    /// When true and the user is an admin, admin-only features are surfaced. Admins can toggle
    /// this off to experience the app as a regular user. Ignored for non-admins.
    /// </summary>
    public bool AdminViewEnabled { get; set; } = true;

    /// <summary>The goal captured on the welcome page at first login. Null until chosen.</summary>
    public GoalType? PrimaryGoal { get; set; }

    /// <summary>Biological sex at birth, captured at first login. Null until provided.</summary>
    public SexAtBirth? SexAtBirth { get; set; }

    /// <summary>
    /// The user's height, stored canonically in centimeters (the UI offers cm or ft/in and converts).
    /// Null until provided.
    /// </summary>
    public double? HeightCm { get; set; }

    /// <summary>
    /// The user's date of birth, captured at first login. Stored as a date (rather than a derived age) so
    /// the user's age stays correct over time; the BMR/Kindle calculation derives age from it and today's
    /// date. Null until provided.
    /// </summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>True once the user has completed the first-login welcome flow.</summary>
    public bool HasCompletedOnboarding { get; set; }

    /// <summary>
    /// The meal plan the user is currently following. The Log uses it to surface today's planned meals
    /// as tasks to complete. Null when the user has not chosen a plan (The Log then works as a plain
    /// free-form log). Not enforced as a foreign key — a stale/deleted plan id simply yields no tasks.
    /// </summary>
    public string? ActiveMealPlanId { get; set; }

    /// <summary>
    /// How often the user wants to log a weight measurement. Null until a schedule is configured (no
    /// recurring "Log your weight" task then appears in The Log). Stored as scalars (cadence + interval +
    /// start date) rather than an owned type to keep Cosmos mapping trivial; <see cref="WeightSchedule"/>
    /// reconstitutes them into the due-date rule.
    /// </summary>
    public WeightScheduleCadence? WeightScheduleCadence { get; set; }

    /// <summary>
    /// The N for the "every N days"/"every N weeks" cadences (≥ 1). Ignored for daily/weekly. Null when no
    /// schedule is set; defaults to 1 when reconstituted if missing.
    /// </summary>
    public int? WeightScheduleInterval { get; set; }

    /// <summary>
    /// The schedule's anchor date: its day-of-week is the chosen "day to start measurements", and it is the
    /// recurrence anchor for the "every N" cadences. Null when no schedule is set.
    /// </summary>
    public DateOnly? WeightScheduleStartDate { get; set; }

    /// <summary>True once a weight-measurement schedule has been configured.</summary>
    public bool HasWeightSchedule => WeightScheduleCadence is not null && WeightScheduleStartDate is not null;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsAdmin => Roles.Contains(AppRole.AppAdmin);

    /// <summary>True if the user is an admin AND currently has the admin view enabled.</summary>
    public bool IsActingAsAdmin => IsAdmin && AdminViewEnabled;

    /// <summary>Canonical lookup key for an external login.</summary>
    public static string LoginKey(string provider, string subject) => $"{provider}:{subject}";

    /// <summary>Links an external provider login if not already present, keeping the index in sync.</summary>
    public void LinkLogin(string provider, string subject)
    {
        var key = LoginKey(provider, subject);
        if (LoginKeys.Contains(key)) return;
        ExternalLogins.Add(new ExternalLogin { Provider = provider, Subject = subject });
        LoginKeys.Add(key);
    }
}

/// <summary>A link between an <see cref="AppUser"/> and an external identity provider account.</summary>
public class ExternalLogin
{
    /// <summary>Provider key, e.g. "google", "microsoft", "facebook", or "dev".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's stable subject/identifier for this user.</summary>
    public string Subject { get; set; } = string.Empty;
}
