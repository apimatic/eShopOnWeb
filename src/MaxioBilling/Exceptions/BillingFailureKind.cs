namespace Microsoft.eShopWeb.MaxioBilling.Exceptions;

/// <summary>
/// The single vocabulary every Maxio failure is translated into at the integration boundary,
/// so callers never have to reason about SDK exception types.
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The <c>Maxio</c> configuration section is missing or incomplete.</summary>
    NotConfigured,

    /// <summary>Configuration is present but does not match the Maxio site (e.g. unknown product family handle).</summary>
    Configuration,

    /// <summary>The requested plan handle does not exist in the configured product family.</summary>
    PlanNotFound,

    /// <summary>Maxio rejected the request; the caller can act on it.</summary>
    Rejected,

    /// <summary>Maxio could not be reached, timed out, or is rate limiting.</summary>
    ProviderUnavailable,

    /// <summary>Maxio replied, but with a server-side failure or a body that could not be read.</summary>
    ProviderError,

    /// <summary>A write may or may not have taken effect and could not be reconciled.</summary>
    OutcomeUnknown
}
