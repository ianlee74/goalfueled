namespace GoalFueled.Domain;

/// <summary>
/// Pure, dependency-free helpers for translating a net daily calorie balance (fuel − burn − kindle) into
/// an estimated change in body weight. Kept free of HTTP/DB concerns so the math — including its sign and
/// unit conventions — is trivially unit-testable.
///
/// <para><b>Approximation:</b> one pound of body fat stores roughly <see cref="CaloriesPerPound"/> kcal, so
/// a cumulative net deficit of that many calories corresponds to ~1 lb lost. This is the classic "3,500 kcal
/// per pound" rule of thumb; it ignores water/glycogen swings and metabolic adaptation, so it is an estimate
/// only.</para>
///
/// <para><b>Sign convention:</b> a net value is <c>fuel − burn − kindle</c> for a day. A <em>negative</em>
/// net is a calorie <em>deficit</em> (more burned than eaten) and yields a <em>positive</em> weight loss; a
/// <em>positive</em> net is a surplus and yields a weight <em>gain</em> (a negative loss). The returned value
/// is "estimated pounds lost", so deficit → positive, surplus → negative.</para>
/// </summary>
public static class WeightProgressEstimator
{
    /// <summary>The classic approximation: ~3,500 kcal of net deficit per pound of body fat.</summary>
    public const double CaloriesPerPound = 3500.0;

    /// <summary>
    /// Estimated pounds of body weight <em>lost</em> from a cumulative net calorie balance. A negative net
    /// (a deficit) returns a positive number (weight lost); a positive net (a surplus) returns a negative
    /// number (weight gained). Divides the deficit (the negated net) by <see cref="CaloriesPerPound"/>.
    /// </summary>
    /// <param name="netCalories">The summed daily net (fuel − burn − kindle) over the period, in kcal.</param>
    public static double PoundsLost(double netCalories) => -netCalories / CaloriesPerPound;

    /// <summary>
    /// The cumulative net over a series of daily nets (their sum), in kcal. A convenience so callers can pass
    /// the per-day series straight through.
    /// </summary>
    public static double CumulativeNet(IEnumerable<double> dailyNets)
    {
        var total = 0.0;
        foreach (var net in dailyNets) total += net;
        return total;
    }

    /// <summary>
    /// Estimated pounds lost over a series of daily nets (sum the series, then convert). Deficit → positive.
    /// </summary>
    public static double PoundsLostOverSeries(IEnumerable<double> dailyNets) =>
        PoundsLost(CumulativeNet(dailyNets));

    /// <summary>Converts an estimate in pounds to the user's preferred unit's magnitude (lb or kg).</summary>
    public static double InUnit(double pounds, WeightUnit unit) => unit switch
    {
        WeightUnit.Pound => pounds,
        WeightUnit.Kilogram => pounds * KindleCostCalculator.KilogramsPerPound,
        _ => pounds,
    };
}
