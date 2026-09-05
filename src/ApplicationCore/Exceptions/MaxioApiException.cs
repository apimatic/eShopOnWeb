using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when Maxio Advanced Billing rejects a request or is otherwise unreachable.
/// </summary>
public class MaxioApiException : Exception
{
    public int? MaxioStatusCode { get; }

    public MaxioApiException(string message, int? maxioStatusCode = null) : base(message)
    {
        MaxioStatusCode = maxioStatusCode;
    }

    public MaxioApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
