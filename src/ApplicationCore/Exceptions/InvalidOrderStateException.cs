using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }

    protected InvalidOrderStateException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
    {
    }
}
