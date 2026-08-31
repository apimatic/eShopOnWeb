using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
