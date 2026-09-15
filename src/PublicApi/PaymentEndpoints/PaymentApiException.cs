using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
