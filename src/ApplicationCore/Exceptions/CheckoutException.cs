using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Domain/application failure that maps to an HTTP status at the PublicApi boundary.
/// Messages must never include card PAN, expiry+CVV, or other payment instrument secrets.
/// </summary>
public class CheckoutException : Exception
{
    public CheckoutException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
