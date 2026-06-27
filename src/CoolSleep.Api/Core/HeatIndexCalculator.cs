namespace CoolSleep.Api.Core;

/// <summary>
/// Formule Steadman/NOAA — valide pour T > 27°C et RH > 40%.
/// </summary>
public static class HeatIndexCalculator
{
    public static double Compute(double tempC, double humidity)
    {
        if (tempC < 27) return tempC;

        double T = tempC;
        double R = humidity;

        return -8.784
             + 1.611  * T
             + 2.338  * R
             - 0.146  * T * R
             - 0.0123 * T * T
             - 0.0164 * R * R
             + 0.00221 * T * T * R
             + 0.00072 * T * R * R
             - 0.000003582 * T * T * R * R;
    }
}
