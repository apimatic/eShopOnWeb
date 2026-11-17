using System;

namespace Microsoft.eShopWeb.PublicApi.Exceptions;

/// <summary>
/// An expected payment/order failure with an HTTP status and a stable machine-readable code.
/// Messages are written for an operator or shopper and never include card data.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
