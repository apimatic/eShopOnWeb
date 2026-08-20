using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioApiException : HttpRequestException
{
    public TwilioApiException(int statusCode, int? errorCode, string message)
        : base(message)
    {
        HttpStatus = statusCode;
        ErrorCode = errorCode;
    }

    public int HttpStatus { get; }
    public int? ErrorCode { get; }
}
