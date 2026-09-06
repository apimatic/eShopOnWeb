namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The recurrence of a subscription plan, e.g. "every 1 month".
/// </summary>
/// <param name="Length">How many <paramref name="Unit"/>s make up one billing period.</param>
/// <param name="Unit">The billing period unit as reported by the billing provider (e.g. "month", "day").</param>
public record BillingInterval(int Length, string Unit)
{
    public static BillingInterval Monthly { get; } = new(1, "month");

    /// <summary>A short human readable rendering such as "month" or "3 months".</summary>
    public string ToDisplayString() => Length == 1 ? Unit : $"{Length} {Unit}s";
}
