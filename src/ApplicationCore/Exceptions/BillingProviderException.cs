using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Billing-provider failure mapped at the Maxio integration boundary.
/// <see cref="HttpStatusCode"/> is the status to return to the PublicApi caller;
/// <see cref="Exception.Message"/> is caller-safe (never an SDK/framework type name).
/// </summary>
public sealed class BillingProviderException : Exception
{
    public BillingProviderException(string message, int httpStatusCode, bool isClientError = false, Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        IsClientError = isClientError;
    }

    public int HttpStatusCode { get; }

    public bool IsClientError { get; }
}
