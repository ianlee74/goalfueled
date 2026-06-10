using GoalFueled.Domain;

namespace GoalFueled.Tests;

/// <summary>
/// Unit tests for the pure net-calorie → estimated-weight-change helper, pinning the sign convention
/// (deficit → loss), the 3,500-kcal-per-pound math, and unit conversion.
/// </summary>
public class WeightProgressEstimatorTests
{
    [Fact]
    public void Deficit_yields_a_positive_weight_loss()
    {
        // A 7,000 kcal net deficit (negative net) ≈ 2 lb lost (positive).
        var lost = WeightProgressEstimator.PoundsLost(-7000);
        Assert.Equal(2.0, lost, 6);
    }

    [Fact]
    public void Surplus_yields_a_negative_weight_loss_a_gain()
    {
        // A 3,500 kcal net surplus (positive net) ≈ 1 lb gained → −1 lb "lost".
        var lost = WeightProgressEstimator.PoundsLost(3500);
        Assert.Equal(-1.0, lost, 6);
    }

    [Fact]
    public void Zero_net_is_zero_change()
    {
        Assert.Equal(0.0, WeightProgressEstimator.PoundsLost(0), 6);
    }

    [Fact]
    public void Cumulative_net_sums_the_daily_series()
    {
        var net = WeightProgressEstimator.CumulativeNet(new[] { -500.0, -500.0, 200.0 });
        Assert.Equal(-800.0, net, 6);
    }

    [Fact]
    public void Pounds_lost_over_series_sums_then_converts_with_deficit_positive()
    {
        // Sum = −3,500 kcal → 1 lb lost.
        var lost = WeightProgressEstimator.PoundsLostOverSeries(new[] { -1000.0, -1500.0, -1000.0 });
        Assert.Equal(1.0, lost, 6);
    }

    [Fact]
    public void In_unit_leaves_pounds_unchanged_and_converts_kilograms()
    {
        Assert.Equal(2.0, WeightProgressEstimator.InUnit(2.0, WeightUnit.Pound), 6);
        // 1 lb ≈ 0.4536 kg.
        Assert.Equal(2.0 * KindleCostCalculator.KilogramsPerPound,
            WeightProgressEstimator.InUnit(2.0, WeightUnit.Kilogram), 6);
    }
}
