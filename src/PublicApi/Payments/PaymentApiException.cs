using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApiException(int statusCode, string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
