namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Why a billing call failed. Each member maps to a different response for the caller, so that a
/// request the caller can fix is never presented the same way as a provider outage.
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The billing integration has no usable credentials, so nothing was attempted.</summary>
    NotConfigured,

    /// <summary>The requested plan, family or customer does not exist in the billing system.</summary>
    NotFound,

    /// <summary>The billing system deterministically rejected the request. Retrying it unchanged cannot succeed.</summary>
    Rejected,

    /// <summary>A competing request won a race. The caller should re-read rather than retry blindly.</summary>
    Conflict,

    /// <summary>The billing system refused our credentials. A deployment problem, not a caller problem.</summary>
    Unauthenticated,

    /// <summary>The billing system was unreachable, timed out, or failed transiently. Retrying may succeed.</summary>
    Unavailable,

    /// <summary>
    /// A write left the provider in an unknown state — it may or may not have taken effect. The caller
    /// must re-read rather than assume either outcome.
    /// </summary>
    UnknownOutcome,

    /// <summary>The billing system answered successfully but the body could not be understood.</summary>
    InvalidResponse
}
