namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingErrorKind
{
    /// <summary>The caller's request is invalid (unknown plan, missing data).</summary>
    InvalidRequest,

    /// <summary>An identical request is already being processed.</summary>
    Conflict,

    /// <summary>The billing system rejected the request on its merits (validation).</summary>
    Rejected,

    /// <summary>The billing system could not be reached, timed out, throttled us or refused our credentials. Retryable.</summary>
    Unavailable,

    /// <summary>The billing system answered with something this application cannot use (misconfiguration, unreadable response).</summary>
    ProviderError
}
