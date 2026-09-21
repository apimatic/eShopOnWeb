using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The single failure type the Twilio adapter presents at its boundary. Carries the provider HTTP status
/// when one was received (a transport failure or unreadable body leaves it null). The message is caller-safe
/// and never contains the shopper's number, the message body, or any credential.
/// </summary>
public class SmsProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public SmsProviderException(string message, HttpStatusCode? statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
