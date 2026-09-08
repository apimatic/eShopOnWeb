using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A subscribable plan surfaced from the Maxio catalog.
/// </summary>
/// <param name="Handle">Stable plan identity (Maxio product handle); used to subscribe.</param>
/// <param name="Name">Display name.</param>
/// <param name="PriceInCents">Recurring price in cents.</param>
/// <param name="Interval">Billing interval count.</param>
/// <param name="IntervalUnit">Billing interval unit ("day" or "month").</param>
public record MaxioPlanInfo(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);

/// <summary>
/// Identity of the eShopOnWeb user on whose behalf a Maxio customer is managed.
/// </summary>
/// <param name="UserId">Stable application-user id — the root of the Maxio customer reference.</param>
/// <param name="UserName">Application username.</param>
/// <param name="Email">Application email, used as the Maxio customer email.</param>
public record MaxioSubscriber(string UserId, string UserName, string Email);

/// <summary>
/// A subscription as confirmed by Maxio.
/// </summary>
/// <param name="SubscriptionId">Maxio subscription id.</param>
/// <param name="PlanHandle">Handle of the subscribed plan.</param>
/// <param name="PlanName">Display name of the subscribed plan.</param>
/// <param name="PriceInCents">Current recurring price in cents.</param>
/// <param name="Interval">Billing interval count.</param>
/// <param name="IntervalUnit">Billing interval unit ("day" or "month").</param>
/// <param name="State">Maxio subscription state (e.g. "active", "trialing").</param>
/// <param name="NextBillingAt">When the current period ends / next billing occurs.</param>
public record MaxioSubscriptionInfo(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

/// <summary>
/// Outcome of a subscribe operation — distinguishes a fresh enrollment from an idempotent replay.
/// </summary>
public record MaxioSubscriptionResult(MaxioSubscriptionInfo Subscription, bool CreatedNew);

/// <summary>
/// The kind of failure a Maxio billing operation failed with; drives the HTTP mapping at the API boundary.
/// </summary>
public enum MaxioBillingErrorKind
{
    /// <summary>The request was rejected by the provider (4xx; deterministic — retrying cannot succeed).</summary>
    InvalidRequest,

    /// <summary>The requested entity (plan / customer) does not exist.</summary>
    NotFound,

    /// <summary>The provider could not be reached or failed with a server-side error.</summary>
    ProviderUnavailable,

    /// <summary>An unexpected failure (malformed response, unknown condition).</summary>
    Unexpected,
}

/// <summary>
/// The one failure type the Maxio billing boundary raises; carries a caller-safe message and the failure kind.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingErrorKind Kind { get; }

    public MaxioBillingException(MaxioBillingErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
