using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
