using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class TwilioGatewayException : Exception
{
    public TwilioGatewayException(string operation, int httpStatus, int? providerErrorCode)
        : base($"Twilio {operation} failed with HTTP {httpStatus}.")
    {
        HttpStatus = httpStatus;
        ProviderErrorCode = providerErrorCode;
    }

    public int HttpStatus { get; }
    public int? ProviderErrorCode { get; }
}
