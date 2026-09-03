using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class PaymentWorkflowException : Exception
{
    public PaymentWorkflowException(int statusCode, string code, string message, string? providerDebugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        ProviderDebugId = providerDebugId;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string? ProviderDebugId { get; }
}
