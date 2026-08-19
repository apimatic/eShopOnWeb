using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Firecrawl call fails or returns a response the integration cannot use.
/// </summary>
public class FirecrawlException : Exception
{
    public FirecrawlException(string message) : base(message)
    {
    }

    public FirecrawlException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
