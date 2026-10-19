using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A plan a shopper can subscribe to.</summary>
/// <param name="Handle">Stable plan handle; the value a subscribe request names.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Price">Recurring price in the site currency (null when the provider did not send one).</param>
/// <param name="Interval">Billing interval length, e.g. 1.</param>
/// <param name="IntervalUnit">Billing interval unit as sent by the provider, e.g. "month".</param>
public sealed record SubscriptionPlan(
    string Handle,
    string? Name,
    string? Description,
    decimal? Price,
    int? Interval,
    string? IntervalUnit);

/// <summary>The available plans.</summary>
/// <param name="Plans">The plans read.</param>
/// <param name="IsComplete">
/// False when the listing stopped at its page cap before the provider signalled the end, so
/// <paramref name="Plans"/> may be missing entries.
/// </param>
public sealed record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);
