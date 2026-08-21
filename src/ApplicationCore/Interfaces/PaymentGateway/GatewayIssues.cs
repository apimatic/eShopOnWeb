namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>
/// Normalized, provider-agnostic issue codes the gateway sets on a
/// <see cref="Exceptions.PaymentGatewayException"/> so the application can branch on them without
/// parsing free-text provider messages.
/// </summary>
public static class GatewayIssues
{
    /// <summary>The authorization has expired and a capture cannot proceed until it is renewed.</summary>
    public const string AuthorizationExpired = "AUTHORIZATION_EXPIRED";

    /// <summary>The authorization can no longer be renewed; a new payment must be collected.</summary>
    public const string AuthorizationNotRenewable = "AUTHORIZATION_CANNOT_BE_RENEWED";
}
