using System;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Raised when a call to the Walmart Marketplace API fails (transport error, non-success status, or an
/// unparseable/invalid response). Messages are built so they never include the client secret or the
/// access token.
/// </summary>
public class WalmartApiException : Exception
{
    public WalmartApiException(string message) : base(message)
    {
    }

    public WalmartApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
