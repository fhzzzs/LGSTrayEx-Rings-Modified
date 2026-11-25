using LGSTrayHID.Protocol;

namespace LGSTrayHID.Features;

/// <summary>
/// Factory for selecting the appropriate battery feature implementation
/// based on what features are supported by a device.
/// </summary>
public static class BatteryFeatureFactory
{
    // Singleton instances of battery feature implementations
    private static readonly Dictionary<ushort, IBatteryFeature> _features = new()
    {
        [HidppFeature.BATTERY_UNIFIED_LEVEL] = new BatteryUnifiedLevel(),
        [HidppFeature.BATTERY_VOLTAGE] = new BatteryVoltage(),
        [HidppFeature.BATTERY_UNIFIED] = new BatteryUnified(),
        [HidppFeature.ADC_MEASUREMENT] = new BatteryAdcMeasurement(),
    };

    /// <summary>
    /// Get the best available battery feature implementation for a device.
    /// </summary>
    /// <param name="featureMap">The device's feature map (feature ID -> feature index mapping)</param>
    /// <returns>
    /// The battery feature implementation, or null if the device doesn't support any battery features.
    /// </returns>
    /// <remarks>
    /// Priority order (from most to least preferred):
    /// 1. Battery Unified Level (0x1000) - Most common, direct percentage
    /// 2. Battery Voltage (0x1001) - Requires voltage lookup table for estimation
    /// 3. Unified Battery (0x1004) - Extended feature with capability info
    /// 4. ADC Measurement (0x1F20) - Direct-connect headsets (G733/G535/G935/PRO X)
    /// </remarks>
    public static IReadOnlyList<IBatteryFeature> GetCandidates(Dictionary<ushort,byte> featureMap)
    {
        // Prefer direct percentage to voltage estimates, then ADC fallback.
        ushort[] order=[HidppFeature.BATTERY_UNIFIED_LEVEL,HidppFeature.BATTERY_UNIFIED,HidppFeature.BATTERY_VOLTAGE,HidppFeature.ADC_MEASUREMENT];
        return order.Where(featureMap.ContainsKey).Select(id=>_features[id]).ToArray();
    }
    public static IBatteryFeature? GetBatteryFeature(Dictionary<ushort, byte> featureMap)
        =>GetCandidates(featureMap).FirstOrDefault();
}
