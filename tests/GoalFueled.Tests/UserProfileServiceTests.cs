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
        return (user!, repo, weights, new UserProfileService(repo, weights));
    }

    private static OnboardingInput Onboarding(
        GoalType goal = GoalType.WeightLoss,
        SexAtBirth sex = SexAtBirth.Male,
        double heightCm = 180,
        double weight = 180,
        WeightUnit unit = WeightUnit.Pound) =>
        new(goal, sex, heightCm, weight, unit);

    [Fact]
    public async Task Onboarding_persists_all_fields_and_marks_complete()
    {
        using var db = TestHelpers.NewContext();
        var (user, repo, _, svc) = await SetupAsync(db);

        await svc.CompleteOnboardingAsync(user,
            Onboarding(GoalType.WeightGain, SexAtBirth.Female, 165.5, 70, WeightUnit.Kilogram), default);

        var saved = await repo.GetByIdAsync(user.Id);
        Assert.NotNull(saved);
        Assert.Equal(GoalType.WeightGain, saved!.PrimaryGoal);
        Assert.Equal(SexAtBirth.Female, saved.SexAtBirth);
        Assert.Equal(165.5, saved.HeightCm);
        Assert.True(saved.HasCompletedOnboarding);
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
            new ProfileInput(GoalType.WeightManagement, SexAtBirth.Female, 170, null, WeightUnit.Pound), default);

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
            new ProfileInput(GoalType.WeightLoss, SexAtBirth.Male, 180, null, WeightUnit.Pound), default);

        Assert.Empty(await weights.ListAsync(user, default));
    }

    [Fact]
    public async Task UpdateProfile_with_weight_upserts_todays_entry()
    {
        using var db = TestHelpers.NewContext();
        var (user, _, weights, svc) = await SetupAsync(db);

        await svc.CompleteOnboardingAsync(user, Onboarding(weight: 180, unit: WeightUnit.Pound), default);
        await svc.UpdateProfileAsync(user,
            new ProfileInput(GoalType.WeightLoss, SexAtBirth.Male, 180, 175, WeightUnit.Pound), default);

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
                new ProfileInput(GoalType.WeightLoss, SexAtBirth.Male, 0, null, WeightUnit.Pound), default));
    }
}
