using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class SubscriptionServiceSettings
{
    /// <summary>Total time one API request may spend waiting on the billing provider.</summary>
    public TimeSpan RequestBudget { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Extra time allowed, after a subscription write went unanswered, to look it up and settle its outcome.
    /// <see cref="RequestBudget"/> + <see cref="SettleBudget"/> is the worst case a caller waits.
    /// </summary>
    public TimeSpan SettleBudget { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>A pending claim older than this is assumed abandoned and is settled by re-reading the provider.</summary>
    public TimeSpan StaleClaimAfter { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Prefix of the customer/subscription references this application sends to the billing provider.</summary>
    public string ReferencePrefix { get; init; } = "eshop";
}
