namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared helpers for presenting subscription pricing/cadence.</summary>
internal static class SubscriptionFormatting
{
    /// <summary>
    /// Turns an interval + unit into a readable cadence: <c>(1, "month") =&gt; "month"</c>,
    /// <c>(3, "month") =&gt; "3 months"</c>.
    /// </summary>
    public static string Frequency(int interval, string? intervalUnit)
    {
        var unit = string.IsNullOrWhiteSpace(intervalUnit) ? "period" : intervalUnit;
        return interval <= 1 ? unit : $"{interval} {unit}s";
    }
}
