using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowException : Exception
{
    public PaymentWorkflowException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
