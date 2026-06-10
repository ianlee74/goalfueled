using GoalFueled.Domain;

namespace GoalFueled.Tests;

/// <summary>
/// Pure unit tests for the Mifflin–St Jeor BMR / Kindle-cost math: the sex constant, lb→kg conversion,
/// age derivation, ×1.2 rounding, and the graceful-degradation contract (<see cref="KindleCostCalculator.TryCompute"/>
/// returns null when any required input is missing).
/// </summary>
public class KindleCostCalculatorTests
{
    [Fact]
    public void SexConstant_is_plus5_for_male_and_minus161_for_female()
    {
        Assert.Equal(5, KindleCostCalculator.SexConstant(SexAtBirth.Male));
        Assert.Equal(-161, KindleCostCalculator.SexConstant(SexAtBirth.Female));
    }

    [Fact]
    public void ToKilograms_converts_pounds_and_passes_kilograms_through()
    {
        Assert.Equal(150 * 0.45359237, KindleCostCalculator.ToKilograms(150, WeightUnit.Pound), 9);
        Assert.Equal(70, KindleCostCalculator.ToKilograms(70, WeightUnit.Kilogram));
    }

    [Fact]
    public void AgeOn_counts_whole_years_and_respects_whether_birthday_has_passed()
    {
        var dob = new DateOnly(1990, 6, 15);
        // Day before the birthday: still the previous age.
        Assert.Equal(35, KindleCostCalculator.AgeOn(dob, new DateOnly(2026, 6, 14)));
        // On the birthday: ticks over.
        Assert.Equal(36, KindleCostCalculator.AgeOn(dob, new DateOnly(2026, 6, 15)));
        // After the birthday.
        Assert.Equal(36, KindleCostCalculator.AgeOn(dob, new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Compute_male_matches_mifflin_st_jeor_times_1point2()
    {
        // BMR = 10*80 + 6.25*180 − 5*30 + 5 = 800 + 1125 − 150 + 5 = 1780; ×1.2 = 2136.
        Assert.Equal(2136, KindleCostCalculator.Compute(80, 180, 30, SexAtBirth.Male));
    }

    [Fact]
    public void Compute_female_matches_mifflin_st_jeor_times_1point2()
    {
        // BMR = 10*60 + 6.25*165 − 5*30 − 161 = 600 + 1031.25 − 150 − 161 = 1320.25; ×1.2 = 1584.3 → 1584.
        Assert.Equal(1584, KindleCostCalculator.Compute(60, 165, 30, SexAtBirth.Female));
    }

    [Fact]
    public void Compute_rounds_to_nearest_whole_calorie_away_from_zero()
    {
        // BMR = 10*70 + 6.25*170 − 5*25 + 5 = 700 + 1062.5 − 125 + 5 = 1642.5; ×1.2 = 1971.0 exactly.
        Assert.Equal(1971, KindleCostCalculator.Compute(70, 170, 25, SexAtBirth.Male));
    }

    [Fact]
    public void TryCompute_converts_pounds_and_derives_age_end_to_end()
    {
        // 176.37 lb ≈ 80 kg, height 180 cm, born 1996-01-01, as-of 2026-06-10 → age 30, male.
        var kindle = KindleCostCalculator.TryCompute(
            SexAtBirth.Male, 180, new DateOnly(1996, 1, 1),
            (176.37, WeightUnit.Pound), new DateOnly(2026, 6, 10));

        // 176.37 lb → 80.0001... kg; Compute(80.0001, 180, 30, Male) ≈ 2136.
        Assert.Equal(2136, kindle);
    }

    [Fact]
    public void TryCompute_returns_null_when_any_required_input_is_missing()
    {
        var asOf = new DateOnly(2026, 6, 10);
        var dob = new DateOnly(1990, 1, 1);
        (double, WeightUnit)? w = (80, WeightUnit.Kilogram);

        Assert.Null(KindleCostCalculator.TryCompute(null, 180, dob, w, asOf));         // no sex
        Assert.Null(KindleCostCalculator.TryCompute(SexAtBirth.Male, null, dob, w, asOf)); // no height
        Assert.Null(KindleCostCalculator.TryCompute(SexAtBirth.Male, 0, dob, w, asOf));    // height ≤ 0
        Assert.Null(KindleCostCalculator.TryCompute(SexAtBirth.Male, 180, null, w, asOf)); // no DOB
        Assert.Null(KindleCostCalculator.TryCompute(SexAtBirth.Male, 180, dob, null, asOf)); // no weight
        Assert.Null(KindleCostCalculator.TryCompute(SexAtBirth.Male, 180, dob, (0, WeightUnit.Kilogram), asOf)); // weight ≤ 0
    }

    [Fact]
    public void TryCompute_returns_null_for_future_dob_or_implausible_age()
    {
        var asOf = new DateOnly(2026, 6, 10);
        (double, WeightUnit)? w = (80, WeightUnit.Kilogram);

        Assert.Null(KindleCostCalculator.TryCompute(
            SexAtBirth.Male, 180, asOf.AddDays(1), w, asOf)); // future DOB → negative age
        Assert.Null(KindleCostCalculator.TryCompute(
            SexAtBirth.Male, 180, asOf.AddYears(-(KindleLimits.MaxAgeYears + 1)), w, asOf)); // too old
    }

    [Fact]
    public void TryCompute_succeeds_with_all_inputs_present()
    {
        var kindle = KindleCostCalculator.TryCompute(
            SexAtBirth.Female, 165, new DateOnly(1996, 6, 10),
            (60, WeightUnit.Kilogram), new DateOnly(2026, 6, 10));

        Assert.Equal(1584, kindle); // age 30, female — same as Compute(60,165,30,Female)
    }
}
