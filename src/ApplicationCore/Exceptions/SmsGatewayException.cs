using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the SMS provider could not complete a request (transport failure or a non-success
/// response). Its <see cref="Exception.Message"/> is deliberately sanitized — it carries the HTTP
/// status and the provider error code only, never credentials or a destination number — so it is
/// safe to log.
/// </summary>
public class SmsGatewayException : Exception
{
    public int? ProviderErrorCode { get; }
    public int? HttpStatusCode { get; }

    public SmsGatewayException(string sanitizedMessage, int? httpStatusCode = null, int? providerErrorCode = null, Exception? inner = null)
        : base(sanitizedMessage, inner)
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
