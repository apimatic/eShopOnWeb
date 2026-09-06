namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Kinds of billing failure the application distinguishes. The kind — not the provider's raw status —
/// is what the API boundary maps to an HTTP status, so that a provider-side credential problem is never
/// reported to the caller as if the caller were unauthorized.
/// </summary>
public enum BillingFailure
{
    /// <summary>The caller asked for something the provider rejected, for example an unknown plan handle.</summary>
    InvalidRequest,

    /// <summary>The requested resource does not exist in the provider.</summary>
    NotFound,

    /// <summary>The provider refused because of a conflicting state.</summary>
    Conflict,

    /// <summary>The provider answered, but with something this application cannot act on.</summary>
    ProviderError,

    /// <summary>The provider could not be reached, or did not answer in time.</summary>
    Unavailable,

    /// <summary>This application is missing or holds invalid billing configuration.</summary>
    Configuration
}
