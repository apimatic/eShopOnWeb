using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class TwilioClientException : Exception
{
    public TwilioClientException(int httpStatus, int? errorCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ErrorCode = errorCode;
    }

    public int HttpStatus { get; }
    public int? ErrorCode { get; }
}
