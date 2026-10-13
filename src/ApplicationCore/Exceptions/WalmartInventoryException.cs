using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the Walmart inventory integration presents to its callers. The
/// infrastructure layer translates every SDK failure (API error, connection, timeout, auth,
/// deserialization) into this type so endpoints have one thing to catch. <see cref="StatusCode"/>
/// carries the provider's HTTP status when one was received, and is the discriminator the API
/// boundary keys on; it is null for transport/auth failures where nothing answered.
/// </summary>
public class WalmartInventoryException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public WalmartInventoryException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
