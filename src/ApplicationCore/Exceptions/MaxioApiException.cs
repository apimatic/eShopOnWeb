using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails or when a request cannot be
/// satisfied against the current Maxio catalog (e.g. an unknown plan handle). Carries the
/// HTTP status the PublicApi should relay to its own caller.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
