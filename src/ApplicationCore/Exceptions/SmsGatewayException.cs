using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the messaging-provider boundary when the provider rejects a request or cannot be reached.
/// Carries the provider's HTTP status when one was returned (null for a transport failure), so a
/// caller-facing boundary can map "our credentials/quota" and transport faults to 5xx while surfacing a
/// genuine caller 4xx as itself. Never carries a shopper's phone number in its message.
/// </summary>
public class SmsGatewayException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public SmsGatewayException(string message, HttpStatusCode? statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public SmsGatewayException(string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = null;
    }
}
