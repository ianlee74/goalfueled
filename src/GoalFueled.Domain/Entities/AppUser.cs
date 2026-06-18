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

    /// <summary>
    /// The user's preferred unit system for body weight and height (kg/cm vs lb/ft-in). Nullable so that
    /// user documents written before this field existed (which have no UnitsPreference property) read back
    /// as null instead of throwing; callers treat null as <see cref="UnitsPreference.UsImperial"/> (the UI's
    /// historical default of lb + ft/in). Newly created users still default to UsImperial. Affects ONLY
    /// weight/height display and entry — never recipe/ingredient units.
    /// </summary>
    // Fully qualified so the initializer binds the enum type, not this same-named property (CS0236).
    public UnitsPreference? UnitsPreference { get; set; } = GoalFueled.Domain.UnitsPreference.UsImperial;

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

    /// <summary>
    /// Ids of the ingredients this user has marked as favorites. Favorites are PER USER and live on
    /// the user document (not on the entity) because ingredients/recipes/meals can be app-level and
    /// shared across users — a flag on the shared entity would leak one user's favorite to everyone.
    /// Nullable so user documents written before this field existed read back as null (treated as
    /// "no favorites") instead of throwing; kept null rather than empty so old docs stay unchanged.
    /// </summary>
    public List<string>? FavoriteIngredientIds { get; set; }

    /// <summary>Ids of the recipes this user has marked as favorites (see <see cref="FavoriteIngredientIds"/>).</summary>
    public List<string>? FavoriteRecipeIds { get; set; }

    /// <summary>Ids of the meals this user has marked as favorites (see <see cref="FavoriteIngredientIds"/>).</summary>
    public List<string>? FavoriteMealIds { get; set; }

    /// <summary>True when the user has favorited the given ingredient. Null list = no favorites.</summary>
    public bool IsFavoriteIngredient(string id) => FavoriteIngredientIds?.Contains(id) == true;

    /// <summary>True when the user has favorited the given recipe. Null list = no favorites.</summary>
    public bool IsFavoriteRecipe(string id) => FavoriteRecipeIds?.Contains(id) == true;

    /// <summary>True when the user has favorited the given meal. Null list = no favorites.</summary>
    public bool IsFavoriteMeal(string id) => FavoriteMealIds?.Contains(id) == true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsAdmin => Roles.Contains(AppRole.AppAdmin);

    /// <summary>
    /// When a guest is "acting as" this account through an accepted share, this is the guest's own user id
    /// (the person actually signed in). Transient — set per request by the authenticator from the active
    /// share grant, never persisted. Null when the signed-in user is operating as themselves.
    /// </summary>
    /// <remarks>
    /// <c>[NotMapped]</c> is required (not just <c>[JsonIgnore]</c>): EF Core maps any read/write property
    /// regardless of the System.Text.Json attribute, so without this the field would be written to — and read
    /// back from — the Cosmos document, and a non-nullable sibling below would throw on existing docs.
    /// </remarks>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ActingViaShareGuestUserId { get; set; }

    /// <summary>
    /// The permissions the acting guest holds over this account (from the share grant). Transient — set per
    /// request alongside <see cref="ActingViaShareGuestUserId"/>, never persisted. Defaults to the full
    /// owner set; only narrowed when a guest is acting via a limited share.
    /// </summary>
    /// <remarks>
    /// <c>[NotMapped]</c> is essential: this is a non-nullable value-type field, so if EF mapped it to the
    /// Cosmos container it would throw on read for every user document persisted before this field existed
    /// (the project's documented "Cosmos non-nullable field" landmine). Being transient request state, it
    /// must never touch the document at all.
    /// </remarks>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public SharePermission ActingSharePermissions { get; set; } =
        SharePermission.ViewLog | SharePermission.ManageMealPlans;

    /// <summary>True when the current request is a guest acting on this account through a share grant.</summary>
    public bool IsActingViaShare => ActingViaShareGuestUserId is not null;

    /// <summary>
    /// True if the user is an admin AND currently has the admin view enabled. An admin operating as another
    /// account via a share is NOT acting as an admin over it — share grants confer only the share's
    /// permissions, never app-admin powers.
    /// </summary>
    public bool IsActingAsAdmin => IsAdmin && AdminViewEnabled && !IsActingViaShare;

    /// <summary>
    /// Whether the current actor may manage (create/edit/delete/assign) this account's meal plans. Always
    /// true when operating as oneself; when acting via a share, only when the grant includes
    /// <see cref="SharePermission.ManageMealPlans"/>.
    /// </summary>
    public bool CanManageMealPlans =>
        !IsActingViaShare || ActingSharePermissions.HasFlag(SharePermission.ManageMealPlans);

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
