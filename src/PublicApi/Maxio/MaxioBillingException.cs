using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type that crosses the Maxio integration boundary. Carries a
/// caller-safe message and, where known, the HTTP status to surface to the API caller.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The HTTP status the endpoint should surface, when one is meaningful.
    /// </summary>
    public int? StatusCode { get; }

    public bool IsClientError => StatusCode is >= 400 and < 500;
}
