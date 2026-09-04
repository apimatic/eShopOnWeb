using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment gateway could not be reached or returned an unusable response.
/// The message is safe to surface; it never contains request payloads or credentials.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message) : base(message) { }

    public PayPalGatewayException(string message, Exception innerException) : base(message, innerException) { }

    #pragma warning disable SYSLIB0051
    protected PayPalGatewayException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
