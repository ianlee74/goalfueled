namespace GoalFueled.Domain;

/// <summary>
/// Pure, dependency-free calculator for the daily <em>Kindle</em> cost: the calories a person burns simply
/// staying alive at rest, surfaced in The Log as a passive <c>burn</c> line. It is the Mifflin–St Jeor
/// Basal Metabolic Rate (BMR) scaled by the sedentary activity factor (1.2).
///
/// <para><b>Mifflin–St Jeor BMR</b> (kg, cm, years):
/// <c>BMR = 10·weightKg + 6.25·heightCm − 5·age + s</c>, where <c>s = +5</c> for male and <c>s = −161</c>
/// for female. <b>Kindle</b> (kcal/day) = <c>round(BMR × 1.2)</c>.</para>
///
/// <para>Kept free of HTTP/DB concerns so the formula, unit conversion, sex constant, age derivation, and
/// ×1.2 rounding can all be unit-tested directly. Callers convert the user's stored profile + latest weight
/// into the primitive inputs and decide graceful-degradation (see <see cref="TryCompute"/>).</para>
/// </summary>
public static class KindleCostCalculator
{
    /// <summary>The sedentary activity factor applied to BMR to estimate at-rest daily energy use.</summary>
    public const double SedentaryActivityFactor = 1.2;

    /// <summary>Kilograms in one pound, for converting a recorded weight to the formula's kg input.</summary>
    public const double KilogramsPerPound = 0.45359237;

    /// <summary>The sex constant <c>s</c> in the Mifflin–St Jeor equation (+5 male, −161 female).</summary>
    public static double SexConstant(SexAtBirth sex) => sex switch
    {
        SexAtBirth.Male => 5,
        SexAtBirth.Female => -161,
        _ => throw new ArgumentOutOfRangeException(nameof(sex), sex, "Unknown sex at birth."),
    };

    /// <summary>Converts a recorded weight value + unit to kilograms (the BMR formula's weight input).</summary>
    public static double ToKilograms(double value, WeightUnit unit) => unit switch
    {
        WeightUnit.Kilogram => value,
        WeightUnit.Pound => value * KilogramsPerPound,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown weight unit."),
    };

    /// <summary>
    /// The user's age in whole years on <paramref name="asOf"/>, derived from their date of birth (the
    /// birthday must have occurred this year to count the current year).
    /// </summary>
    public static int AgeOn(DateOnly dateOfBirth, DateOnly asOf)
    {
        var age = asOf.Year - dateOfBirth.Year;
        if (dateOfBirth > asOf.AddYears(-age)) age--;
        return age;
    }

    /// <summary>The raw Mifflin–St Jeor BMR (kcal/day) for the given inputs (not rounded, not ×1.2).</summary>
    public static double Bmr(double weightKg, double heightCm, int ageYears, SexAtBirth sex) =>
        (10 * weightKg) + (6.25 * heightCm) - (5 * ageYears) + SexConstant(sex);

    /// <summary>The Kindle cost in kcal/day: <c>round(BMR × 1.2)</c>, rounded to the nearest whole calorie.</summary>
    public static int Compute(double weightKg, double heightCm, int ageYears, SexAtBirth sex) =>
        (int)Math.Round(Bmr(weightKg, heightCm, ageYears, sex) * SedentaryActivityFactor,
            MidpointRounding.AwayFromZero);

    /// <summary>
    /// Computes the Kindle cost from a user's raw profile, or returns null when any required input is missing
    /// or non-sensical — the graceful-degradation contract The Log relies on. Inputs are unavailable when:
    /// there is no sex, no height (or ≤ 0), no date of birth, no recorded weight (or ≤ 0), or the derived age
    /// is not in a plausible range. A null result tells callers to prompt "complete your profile" rather than
    /// show a wrong/zero number.
    /// </summary>
    /// <param name="sex">The user's sex at birth (null if not yet captured).</param>
    /// <param name="heightCm">The user's height in cm (null if not yet captured).</param>
    /// <param name="dateOfBirth">The user's date of birth (null if not yet captured).</param>
    /// <param name="latestWeight">The user's most-recent recorded weight value + unit (null if none recorded).</param>
    /// <param name="asOf">The date to derive age on (typically today).</param>
    public static int? TryCompute(
        SexAtBirth? sex,
        double? heightCm,
        DateOnly? dateOfBirth,
        (double Value, WeightUnit Unit)? latestWeight,
        DateOnly asOf)
    {
        if (sex is not { } s || !Enum.IsDefined(typeof(SexAtBirth), s)) return null;
        if (heightCm is not { } h || !(h > 0)) return null;
        if (dateOfBirth is not { } dob) return null;
        if (latestWeight is not { } w || !(w.Value > 0)) return null;

        var age = AgeOn(dob, asOf);
        // Guard against a future DOB or an implausible age (mirrors the profile validation bounds).
        if (age < 0 || age > KindleLimits.MaxAgeYears) return null;

        return Compute(ToKilograms(w.Value, w.Unit), h, age, s);
    }
}

/// <summary>Plausibility bounds for the date-of-birth / age inputs to the Kindle calculation.</summary>
public static class KindleLimits
{
    /// <summary>The maximum plausible human age in years (a generous ceiling for validation).</summary>
    public const int MaxAgeYears = 130;
}
