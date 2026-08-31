using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown by the payment gateway when the processor (PayPal) rejects a call.
/// Carries the processor's error detail in a form safe to surface to operators.
/// Never contains card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? httpStatusCode = null, string? debugId = null,
        bool isAuthorizationUnusable = false, bool isNotFound = false)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        DebugId = debugId;
        IsAuthorizationUnusable = isAuthorizationUnusable;
        IsNotFound = isNotFound;
    }

    public int? HttpStatusCode { get; }
    public string? DebugId { get; }

    /// <summary>The authorization has expired or is otherwise no longer capturable; a renewal may be possible.</summary>
    public bool IsAuthorizationUnusable { get; }

    /// <summary>The referenced resource does not exist at the processor.</summary>
    public bool IsNotFound { get; }
}
