using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioApiException : Exception
{
    public TwilioApiException(string message) : base(message)
    {
    }

    public TwilioApiException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public int? HttpStatus { get; init; }
    public int? TwilioErrorCode { get; init; }
}
