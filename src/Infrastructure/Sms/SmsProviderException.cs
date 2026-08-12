using System;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when a provider call fails in a way the caller must know about (a non-success HTTP status
/// on an operation other than sending). Its message is deliberately free of any secret or destination
/// number so it is safe to surface and log.
/// </summary>
public class SmsProviderException : Exception
{
    public int? StatusCode { get; }
    public int? ProviderErrorCode { get; }

    public SmsProviderException(string message, int? statusCode = null, int? providerErrorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
