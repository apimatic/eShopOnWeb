using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderStateException : Exception
{
    public OrderStateException(string message) : base(message) { }

    public OrderStateException(string message, Exception innerException) : base(message, innerException) { }

    #pragma warning disable SYSLIB0051
    protected OrderStateException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
