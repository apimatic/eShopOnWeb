using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at fulfilment when a stale authorization can no longer be renewed, so the capture cannot
/// proceed. The message is phrased for an operator to act on (re-request payment from the shopper).
/// </summary>
public class AuthorizationUnrenewableException : Exception
{
    public int OrderId { get; }
    public string AuthorizationId { get; }

    public AuthorizationUnrenewableException(int orderId, string authorizationId, string message, Exception? inner = null)
        : base(message, inner)
    {
        OrderId = orderId;
        AuthorizationId = authorizationId;
    }
}
