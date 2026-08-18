using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single failure type the SMS gateway raises for genuine provider/transport faults. It carries
/// the provider's HTTP status where one exists (null for transport failures — nothing answered), so a
/// boundary can map a provider 4xx the caller can act on differently from an outage. Its message is
/// always caller-safe and never contains a secret or a shopper's number.
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
        : this(message, null, inner)
    {
    }
}
