using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum MaxioBillingFailure
{
    /// <summary>Caller input is invalid (e.g. no plan chosen).</summary>
    InvalidRequest,
    /// <summary>The plan is not one this application offers.</summary>
    UnknownPlan,
    /// <summary>The user already has an equivalent subscription request in flight or settled differently.</summary>
    InProgress,
    /// <summary>The billing provider rejected the request.</summary>
    Rejected,
    /// <summary>The billing provider is unreachable, failing, or timed out.</summary>
    ProviderUnavailable
}

public class MaxioBillingException : Exception
{
    public MaxioBillingFailure Failure { get; }

    public MaxioBillingException(MaxioBillingFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }
}
