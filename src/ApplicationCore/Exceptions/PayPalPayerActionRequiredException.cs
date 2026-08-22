using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper approval step in a browser (for example 3-D Secure).
/// This integration does not implement that round-trip.
/// </summary>
public class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string message) : base(message)
    {
    }
}
