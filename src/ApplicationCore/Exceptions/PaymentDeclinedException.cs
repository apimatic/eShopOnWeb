using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal declined the payment instrument. The message is safe to show the shopper.
/// </summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }

    public PaymentDeclinedException(string message, Exception innerException) : base(message, innerException) { }

    #pragma warning disable SYSLIB0051
    protected PaymentDeclinedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
