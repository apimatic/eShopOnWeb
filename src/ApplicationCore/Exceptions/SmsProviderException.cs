using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the messaging integration surfaces to the rest of the application.
/// The provider wrapper converts every underlying transport / API / deserialization failure into
/// this type, carrying a caller-safe message only — never the provider's raw exception text and
/// never a shopper's phone number. When the failure came from a provider HTTP response,
/// <see cref="StatusCode"/> carries that status so callers can distinguish a client-side rejection
/// (a 4xx — e.g. an unusable number) from a provider outage (a 5xx / transport failure).
/// </summary>
public class SmsProviderException : Exception
{
    public int? StatusCode { get; }

    public SmsProviderException(string message) : base(message)
    {
    }

    public SmsProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public SmsProviderException(string message, int? statusCode, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
