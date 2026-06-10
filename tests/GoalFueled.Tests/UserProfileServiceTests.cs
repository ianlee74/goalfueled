using GoalFueled.Api.Auth;
using GoalFueled.Api.Common;
using GoalFueled.Api.Services;
using GoalFueled.Data;
using GoalFueled.Data.Repositories;
using GoalFueled.Domain;
using GoalFueled.Domain.Contracts;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Tests;

public class UserProfileServiceTests
{
    private static async Task<(AppUser user, UserRepository repo, WeightService weights, UserProfileService svc)>
        SetupAsync(GoalFueledDbContext db)
    {
        var user = await TestHelpers.NewAuthenticator(db,
            new AuthenticatedIdentity("dev", "user", "user@dev.local", "User"))
            .GetCurrentUserAsync(TestHelpers.BlankRequest());
        var repo = new UserRepository(db);
        var weights = new WeightService(new WeightEntryRepository(db));
        var plans = new MealPlanService(
            new MealPlanRepository(db), new MealRepository(db),
            new RecipeRepository(db), new IngredientRepository(db));
        return (user!, repo, weights, new UserProfileService(repo, weights, plans));
    }

    private static readonly DateOnly DefaultDob = new(1990, 1, 1);

    private static OnboardingInput Onboarding(
        GoalType goal = GoalType.WeightLoss,
        SexAtBirth sex = SexAtBirth.Male,
        double heightCm = 180,
        double weight = 180,
        WeightUnit unit = WeightUnit.Pound,
        DateOnly? dob = null) =>
        new(goal, sex, heightCm, dob ?? DefaultDob, weight, unit);

    [Fact]
    public async Task Onboarding_persists_all_fields_and_marks_complete()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, _, svc) = await SetupAsync(db);

        var dob = new DateOnly(1985, 7, 20);
        await svc.CompleteOnboardingAsync(user,
            Onboarding(GoalType.WeightGain, SexAtBirth.Female, 165.5, 70, WeightUnit.Kilogram, dob), default);

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.NotNull(saved);
        Assert.Equal(GoalType.WeightGain, saved!.PrimaryGoal);
        Assert.Equal(SexAtBirth.Female, saved.SexAtBirth);
        Assert.Equal(165.5, saved.HeightCm);
        Assert.Equal(dob, saved.DateOfBirth);
        Assert.True(saved.HasCompletedOnboarding);
    }

    [Fact]
    public async Task Onboarding_rejects_future_date_of_birth_and_leaves_onboarding_incomplete()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, weights, svc) = await SetupAsync(db);
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CompleteOnboardingAsync(user, Onboarding(dob: future), default));

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.False(saved!.HasCompletedOnboarding);
        Assert.Null(saved.DateOfBirth);
        Assert.Empty(await weights.ListAsync(user, default));
    }

    [Fact]
    public async Task Onboarding_rejects_implausibly_old_date_of_birth()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, svc) = await SetupAsync(db);
        var tooOld = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-(KindleLimits.MaxAgeYears + 1));

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CompleteOnboardingAsync(user, Onboarding(dob: tooOld), default));
    }

    [Fact]
    public async Task UpdateProfile_persists_date_of_birth()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, _, svc) = await SetupAsync(db);
        var dob = new DateOnly(1979, 3, 14);

        await svc.CompleteOnboardingAsync(user, Onboarding(), default);
        await svc.UpdateProfileAsync(user,
            new ProfileInput(GoalType.WeightManagement, SexAtBirth.Female, 170, dob, null, WeightUnit.Pound), default);

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.Equal(dob, saved!.DateOfBirth);
    }

    [Fact]
    public async Task Onboarding_records_current_weight_as_todays_entry()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights, svc) = await SetupAsync(db);

        await svc.CompleteOnboardingAsync(user, Onboarding(weight: 185, unit: WeightUnit.Pound), default);

        var history = await weights.ListAsync(user, default);
        Assert.Single(history);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), history[0].Date);
        Assert.Equal(185, history[0].Value);
        Assert.Equal(WeightUnit.Pound, history[0].Unit);
    }

    [Fact]
    public async Task Onboarding_rejects_non_positive_weight_and_leaves_onboarding_incomplete()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, weights, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CompleteOnboardingAsync(user, Onboarding(weight: 0), default));

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.False(saved!.HasCompletedOnboarding);
        Assert.Empty(await weights.ListAsync(user, default));
    }

    [Fact]
    public async Task Onboarding_rejects_non_positive_height()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CompleteOnboardingAsync(user, Onboarding(heightCm: 0), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CompleteOnboardingAsync(user, Onboarding(heightCm: -10), default));
    }

    [Fact]
    public async Task Onboarding_rejects_unknown_enum_values()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CompleteOnboardingAsync(user, Onboarding(goal: (GoalType)99), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CompleteOnboardingAsync(user, Onboarding(sex: (SexAtBirth)99), default));
    }

    [Fact]
    public async Task UpdateProfile_changes_fields()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, _, svc) = await SetupAsync(db);

        await svc.CompleteOnboardingAsync(user, Onboarding(), default);
        await svc.UpdateProfileAsync(user,
            new ProfileInput(GoalType.WeightManagement, SexAtBirth.Female, 170, DefaultDob, null, WeightUnit.Pound), default);

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.Equal(GoalType.WeightManagement, saved!.PrimaryGoal);
        Assert.Equal(SexAtBirth.Female, saved.SexAtBirth);
        Assert.Equal(170, saved.HeightCm);
    }

    [Fact]
    public async Task UpdateProfile_without_weight_does_not_create_entry()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights, svc) = await SetupAsync(db);

        await svc.UpdateProfileAsync(user,
            new ProfileInput(GoalType.WeightLoss, SexAtBirth.Male, 180, DefaultDob, null, WeightUnit.Pound), default);

        Assert.Empty(await weights.ListAsync(user, default));
    }

    [Fact]
    public async Task UpdateProfile_with_weight_upserts_todays_entry()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights, svc) = await SetupAsync(db);

        await svc.CompleteOnboardingAsync(user, Onboarding(weight: 180, unit: WeightUnit.Pound), default);
        await svc.UpdateProfileAsync(user,
            new ProfileInput(GoalType.WeightLoss, SexAtBirth.Male, 180, DefaultDob, 175, WeightUnit.Pound), default);

        var history = await weights.ListAsync(user, default);
        Assert.Single(history); // upserted today's entry, not duplicated
        Assert.Equal(175, history[0].Value);
    }

    [Fact]
    public async Task UpdateProfile_rejects_invalid_height()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.UpdateProfileAsync(user,
                new ProfileInput(GoalType.WeightLoss, SexAtBirth.Male, 0, DefaultDob, null, WeightUnit.Pound), default));
    }

    // ---- weight-measurement schedule ----

    [Fact]
    public async Task SetWeightSchedule_persists_an_every_n_weeks_schedule()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, _, svc) = await SetupAsync(db);
        var start = new DateOnly(2026, 6, 10); // Wednesday

        await svc.SetWeightScheduleAsync(user,
            new WeightScheduleInput(WeightScheduleCadence.EveryNWeeks, start, 2), default);

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.Equal(WeightScheduleCadence.EveryNWeeks, saved!.WeightScheduleCadence);
        Assert.Equal(start, saved.WeightScheduleStartDate);
        Assert.Equal(2, saved.WeightScheduleInterval);
        Assert.True(saved.HasWeightSchedule);
    }

    [Fact]
    public async Task SetWeightSchedule_defaults_start_date_to_today_and_interval_to_one()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, _, svc) = await SetupAsync(db);

        await svc.SetWeightScheduleAsync(user,
            new WeightScheduleInput(WeightScheduleCadence.Daily), default);

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.Equal(WeightScheduleCadence.Daily, saved!.WeightScheduleCadence);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), saved.WeightScheduleStartDate);
        Assert.Equal(1, saved.WeightScheduleInterval); // daily ignores any supplied interval
    }

    [Fact]
    public async Task SetWeightSchedule_null_cadence_clears_an_existing_schedule()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, _, svc) = await SetupAsync(db);

        await svc.SetWeightScheduleAsync(user,
            new WeightScheduleInput(WeightScheduleCadence.Weekly), default);
        await svc.SetWeightScheduleAsync(user, new WeightScheduleInput(null), default);

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.Null(saved!.WeightScheduleCadence);
        Assert.Null(saved.WeightScheduleStartDate);
        Assert.Null(saved.WeightScheduleInterval);
        Assert.False(saved.HasWeightSchedule);
    }

    [Fact]
    public async Task SetWeightSchedule_rejects_invalid_interval_for_every_n_cadences()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.SetWeightScheduleAsync(user,
                new WeightScheduleInput(WeightScheduleCadence.EveryNDays, null, 0), default));
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.SetWeightScheduleAsync(user,
                new WeightScheduleInput(WeightScheduleCadence.EveryNWeeks, null, InputLimits.WeightScheduleIntervalMax + 1), default));
    }

    [Fact]
    public async Task SetWeightSchedule_rejects_unknown_cadence()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, _, svc) = await SetupAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.SetWeightScheduleAsync(user, new WeightScheduleInput((WeightScheduleCadence)99), default));
    }
}
