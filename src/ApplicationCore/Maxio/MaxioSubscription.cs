using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioSubscription
{
    public int Id { get; init; }
    public required string State { get; init; }
    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }
    public int PriceInCents { get; init; }

    /// <summary>
    /// End of the current billing period, i.e. when the next charge will be assessed.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }
}
