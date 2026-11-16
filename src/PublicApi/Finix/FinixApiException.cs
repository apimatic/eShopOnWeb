using System;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public class FinixApiException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }
    public string? RawResponse { get; }

    public FinixApiException(int statusCode, string? code, string message, string? rawResponse = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        RawResponse = rawResponse;
    }
}
