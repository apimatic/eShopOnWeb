using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raised when a Firecrawl call cannot be completed or its response cannot be understood.
/// </summary>
public class FirecrawlException : Exception
{
    public FirecrawlException(string message) : base(message) { }
    public FirecrawlException(string message, Exception innerException) : base(message, innerException) { }
}
