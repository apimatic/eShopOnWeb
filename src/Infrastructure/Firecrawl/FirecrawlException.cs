using System;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Raised when a Firecrawl API call fails — a non-success HTTP status (the spec's error model is
/// <c>{ "error": string }</c>), or an extract job that ends in a failed/cancelled state.
/// </summary>
public class FirecrawlException : Exception
{
    public int? StatusCode { get; }

    public FirecrawlException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
