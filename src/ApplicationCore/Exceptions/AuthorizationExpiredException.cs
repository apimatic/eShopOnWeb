using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown by the payment gateway when a capture is rejected specifically because the
/// authorization's honor period has elapsed. Distinct from <see cref="PaymentDeclinedException"/>
/// so the caller knows to attempt a reauthorization rather than treat this as a final decline.
/// </summary>
public class AuthorizationExpiredException : Exception
{
    public AuthorizationExpiredException(string message) : base(message)
    {
    }
}
