using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>PayPal declined or could not perform the requested payment operation.</summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}
