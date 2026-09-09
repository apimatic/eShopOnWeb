using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The failure kinds a Maxio billing operation can produce. Endpoints map each
/// kind to a distinct HTTP outcome; the kinds deliberately keep "the provider
/// rejected this request" (client can act) apart from "the provider could not
/// answer" (retrying may help).
/// </summary>
public enum MaxioBillingError
{
    /// <summary>Maxio rejected the request (4xx). The caller can act on it.</summary>
    ProviderRejected,

    /// <summary>Maxio could not be reached (transport failure / timeout).</summary>
    ProviderUnreachable,

    /// <summary>Maxio returned an error that carries no actionable detail (5xx/unknown).</summary>
    ProviderError,

    /// <summary>Maxio answered but the body could not be read — the outcome is unknown.</summary>
    UnreadableResponse,

    /// <summary>The requested plan does not exist in the configured product family.</summary>
    PlanNotFound,

    /// <summary>The configured product family does not exist — a configuration problem.</summary>
    FamilyNotFound
}

/// <summary>
/// Raised when a Maxio billing operation fails. Carries a caller-safe message
/// only; provider exception internals never reach the wire.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingError Error { get; }

    public MaxioBillingException(MaxioBillingError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }
}
