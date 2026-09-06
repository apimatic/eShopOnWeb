using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the references this application stamps onto the Maxio records it owns.
/// </summary>
/// <remarks>
/// <para>
/// References are the backbone of the integration's idempotency. Maxio enforces uniqueness of
/// <c>reference</c> per site for both customers and subscriptions, so a deterministic reference
/// turns "create" into "create at most once" without any local bookkeeping — which matters here,
/// because eShopOnWeb stores no copy of the billing state and may be running on an in-memory
/// database that is wiped on restart.
/// </para>
/// <para>
/// They are also readable: an operator can find a shopper's records in the Maxio UI by searching
/// for their email address.
/// </para>
/// </remarks>
public static class MaxioReferences
{
    /// <summary>
    /// The reference for a shopper's Maxio customer record, e.g. <c>eshoponweb:demouser@microsoft.com</c>.
    /// </summary>
    public static string Customer(string prefix, string subscriberKey) =>
        $"{Normalize(prefix)}:{Normalize(subscriberKey)}";

    /// <summary>
    /// The reference for a shopper's enrolment in a plan, e.g.
    /// <c>eshoponweb:demouser@microsoft.com:pro-plan</c>.
    /// </summary>
    /// <param name="generation">
    /// How many subscriptions the shopper already has for this plan. Zero — the first, and the only
    /// value the hero flow ever uses — produces the unsuffixed reference. Later generations are
    /// suffixed so a shopper whose earlier subscription ended can enrol again without colliding
    /// with the retired record.
    /// </param>
    public static string Subscription(string prefix, string subscriberKey, string planHandle, int generation = 0)
    {
        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "Generation cannot be negative.");
        }

        var root = $"{Normalize(prefix)}:{Normalize(subscriberKey)}:{Normalize(planHandle)}";

        return generation == 0
            ? root
            : $"{root}:{generation.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Lower-cases and trims a reference part so the same shopper always maps to the same record,
    /// however their email was cased at sign-in.
    /// </summary>
    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
