using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowException : Exception
{
    public PaymentWorkflowException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
