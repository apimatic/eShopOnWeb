using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS-provider integration boundary raises. It carries the
/// provider's HTTP status (when the provider answered) so the API layer can map a provider
/// 4xx the caller can act on back to a client 4xx, and a transport/unknown failure to a 5xx.
/// Its message is always caller-safe — it never carries a raw provider/SDK/JSON exception string
/// and never carries the destination number.
/// </summary>
public class SmsNotificationException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public SmsNotificationException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
