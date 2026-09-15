using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the messaging provider rejects or errors on a request. Carries the provider's
/// error code where one is available so callers can record and log the outcome without
/// depending on the provider's SDK or leaking message contents.
/// </summary>
public class SmsGatewayException : Exception
{
    public int? ErrorCode { get; }

    public SmsGatewayException(string message, int? errorCode = null) : base(message)
    {
        ErrorCode = errorCode;
    }

    public SmsGatewayException(string message, Exception innerException, int? errorCode = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
