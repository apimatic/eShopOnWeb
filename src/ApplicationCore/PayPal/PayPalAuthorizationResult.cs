using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public class PayPalAuthorizationResult
{
    public PayPalAuthorizationResult(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        Status = status;
        ExpiresAt = expiresAt;
    }

    public string PayPalOrderId { get; }
    public string AuthorizationId { get; }
    public string Status { get; }
    public DateTimeOffset? ExpiresAt { get; }
}
