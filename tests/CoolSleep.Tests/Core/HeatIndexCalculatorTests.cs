namespace CoolSleep.Tests.Core;

using CoolSleep.Api.Core;
using FluentAssertions;

public class HeatIndexCalculatorTests
{
    [Fact]
    public void BelowThreshold_ReturnsTempAsIs()
    {
        var result = HeatIndexCalculator.Compute(25, 60);
        result.Should().Be(25);
    }

    [Fact]
    public void AboveThreshold_ReturnsHigherThanAirTemp()
    {
        var result = HeatIndexCalculator.Compute(35, 70);
        result.Should().BeGreaterThan(35);
    }

    [Theory]
    [InlineData(38, 80)]
    [InlineData(32, 50)]
    public void HighHumidity_IncreasesFeelsLike(double temp, double humidity)
    {
        var result = HeatIndexCalculator.Compute(temp, humidity);
        result.Should().BeGreaterThanOrEqualTo(temp);
    }
}
