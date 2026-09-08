using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A Maxio subscription-billing failure that has been translated at the integration
/// boundary into an HTTP status code plus a caller-safe public message. The
/// exception <see cref="Exception.Message"/> carries the internal detail (for logs);
/// only <see cref="PublicMessage"/> is ever placed on the wire.
/// </summary>
public class MaxioSubscriptionException : Exception
{
    /// <summary>HTTP status code the PublicApi should return to the caller.</summary>
    public int StatusCode { get; }

    /// <summary>Caller-safe message suitable for an error response body.</summary>
    public string PublicMessage { get; }

    public MaxioSubscriptionException(int statusCode, string publicMessage)
        : base(publicMessage)
    {
        StatusCode = statusCode;
        PublicMessage = publicMessage;
    }

    public MaxioSubscriptionException(int statusCode, string publicMessage, string detail)
        : base(detail)
    {
        StatusCode = statusCode;
        PublicMessage = publicMessage;
    }

    public MaxioSubscriptionException(int statusCode, string publicMessage, string detail, Exception? innerException)
        : base(detail, innerException)
    {
        StatusCode = statusCode;
        PublicMessage = publicMessage;
    }
}
