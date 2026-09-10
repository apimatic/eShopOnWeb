using System;

namespace MaxioAdvancedBilling.Core.Exceptions;

public abstract class SseException : Exception
{
    private protected SseException(string message) : base(message)
    {
    }

    private protected SseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
