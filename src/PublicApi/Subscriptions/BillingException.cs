using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public enum BillingFailure
{
    InvalidRequest,   // caller sent something we cannot accept
    NotFound,
    Conflict,         // same subscribe already in progress
    Unavailable,      // provider unreachable / our credentials or quota; nothing happened, safe to retry
    Unknown           // the outcome could not be settled; a later call reconciles
}

/// <summary>A caller-safe failure; never carries SDK type names or provider internals.</summary>
public class BillingException : Exception
{
    public BillingFailure Failure { get; }

    public BillingException(BillingFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    public int StatusCode => Failure switch
    {
        BillingFailure.InvalidRequest => 422,
        BillingFailure.NotFound => 404,
        BillingFailure.Conflict => 409,
        BillingFailure.Unavailable => 503,
        _ => 502
    };
}
