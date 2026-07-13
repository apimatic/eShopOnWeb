using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class PlanChangeTimingParser
{
    /// <summary>
    /// Parses the wire value of a plan-change timing request. Defaults to <see cref="PlanChangeTiming.Immediate"/>
    /// when unspecified — the only timing the Maxio SDK exposes (see <see cref="PlanChangeNotSupportedException"/>).
    /// </summary>
    public static PlanChangeTiming Parse(string? timing)
    {
        if (string.IsNullOrWhiteSpace(timing))
        {
            return PlanChangeTiming.Immediate;
        }

        if (System.Enum.TryParse<PlanChangeTiming>(timing, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new PlanChangeNotSupportedException($"Unknown plan-change timing '{timing}'. Valid values: Immediate, AtNextRenewal.");
    }
}
