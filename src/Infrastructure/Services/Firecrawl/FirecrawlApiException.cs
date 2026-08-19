using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>Raised when a Firecrawl API call returns an error response or an unusable payload.</summary>
public class FirecrawlApiException : Exception
{
    public FirecrawlApiException(string message) : base(message) { }
}
