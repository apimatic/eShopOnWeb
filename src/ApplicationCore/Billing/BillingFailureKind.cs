namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Why a billing operation failed. The kinds are deliberately distinct because they map to
/// different HTTP responses: collapsing them all into one status would tell a retrying caller to
/// keep retrying a request that can never succeed.
/// </summary>
public enum BillingFailureKind
{
    /// <summary>No billing credentials/catalog are configured for this deployment.</summary>
    NotConfigured,

    /// <summary>The caller asked for something the provider refused — bad input, not a fault.</summary>
    Rejected,

    /// <summary>The requested plan, customer or subscription does not exist.</summary>
    NotFound,

    /// <summary>A concurrent request for the same shopper is already in flight.</summary>
    Conflict,

    /// <summary>The provider could not be reached, timed out, or returned a server error.</summary>
    Unavailable,

    /// <summary>The provider answered, but the response body could not be read.</summary>
    UnreadableResponse
}
