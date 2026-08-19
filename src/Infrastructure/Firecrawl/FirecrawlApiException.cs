using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Raised when a Firecrawl API call returns a non-success response. The message is taken from
/// the error models declared in the Firecrawl OpenAPI specification (the <c>error</c> field,
/// and the <c>code</c> field when present on 5xx responses).
/// </summary>
public class FirecrawlApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ErrorCode { get; }

    public FirecrawlApiException(HttpStatusCode statusCode, string? errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
