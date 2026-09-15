using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalException : Exception
{
    public PayPalException(HttpStatusCode statusCode, string providerCode, string message,
        string? debugId = null) : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string ProviderCode { get; }
    public string? DebugId { get; }
}

public sealed class PayPalPayerActionRequiredException : PayPalException
{
    public PayPalPayerActionRequiredException(string message, string? debugId = null)
        : base(HttpStatusCode.UnprocessableEntity, "PAYER_ACTION_REQUIRED", message, debugId) { }
}

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
