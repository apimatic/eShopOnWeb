using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A shopper's saved payment method either does not exist or does not belong to the caller.
/// The two cases are deliberately not distinguished towards the caller.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId) : base($"Payment method {paymentMethodId} was not found.") { }

    public PaymentMethodNotFoundException(string message) : base(message) { }

    #pragma warning disable SYSLIB0051
    protected PaymentMethodNotFoundException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
