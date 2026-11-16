using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
