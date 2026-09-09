using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the subscription-billing integration surfaces to its callers. It
/// deliberately carries no provider/SDK exception detail in its message — only a caller-safe
/// description — plus enough classification for the API boundary to choose a coherent HTTP status:
/// <list type="bullet">
/// <item><see cref="IsCallerError"/> — the caller's request was rejected (bad plan, validation) and
/// can be acted on; maps to a 4xx.</item>
/// <item>otherwise the provider is unavailable or the outcome is unknown; maps to a 5xx.</item>
/// </list>
/// </summary>
public class BillingException : Exception
{
    public bool IsCallerError { get; }

    /// <summary>Provider HTTP status where one was available; otherwise null.</summary>
    public int? ProviderStatusCode { get; }

    public BillingException(string message, bool isCallerError = false, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        IsCallerError = isCallerError;
        ProviderStatusCode = providerStatusCode;
    }
}
