using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Maxio Advanced Billing operation cannot be completed. Carries an HTTP
/// status code so the API layer can surface a meaningful response to the caller.
/// </summary>
public class MaxioIntegrationException : Exception
{
    /// <summary>Suggested HTTP status code to return to the API caller.</summary>
    public int StatusCode { get; }

    public MaxioIntegrationException(string message, int statusCode = 502)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioIntegrationException(string message, Exception innerException, int statusCode = 502)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
