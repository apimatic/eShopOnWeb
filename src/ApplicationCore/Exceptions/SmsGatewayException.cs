using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS gateway abstraction raises. All provider/transport failures from the
/// underlying SDK (API errors, unreachable host, timeouts, unreadable bodies) are translated to this at the
/// gateway boundary, so callers have one type to handle. Carries the provider HTTP status where one exists.
/// </summary>
public class SmsGatewayException : Exception
{
    public int? StatusCode { get; }

    public SmsGatewayException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
