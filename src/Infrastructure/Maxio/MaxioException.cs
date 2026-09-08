using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Base type for every error the Maxio integration raises at its boundary. Messages on these
/// exceptions are caller-safe (they never embed SDK/framework type names or secrets).
/// </summary>
public abstract class MaxioException : Exception
{
    protected MaxioException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a Maxio call is attempted but the Maxio settings are missing or incomplete.
/// Maps to HTTP 503.
/// </summary>
public class MaxioNotConfiguredException : MaxioException
{
    public MaxioNotConfiguredException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when Maxio answers with a non-success status (or our boundary cannot process the
/// response). Carries the HTTP status to surface to the caller: provider 4xx statuses are
/// preserved so the caller can act on them; provider/transport failures are normalized to 5xx.
/// </summary>
public class MaxioApiException : MaxioException
{
    public MaxioApiException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status this failure should surface as.</summary>
    public int StatusCode { get; }
}
