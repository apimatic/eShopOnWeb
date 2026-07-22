using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the billing provider failed. Thrown only by the single Infrastructure billing client,
/// so the rest of the application never sees a provider specific exception type.
/// </summary>
/// <remarks>
/// Messages carried by this exception are safe to surface to a caller: the billing client redacts
/// credentials and never copies raw provider payloads into them.
/// </remarks>
public class BillingProviderException : Exception
{
    private static readonly IReadOnlyList<string> NoErrors = Array.Empty<string>();

    public BillingProviderException(string message, string operation, int? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors ?? NoErrors;
    }

    /// <summary>The logical billing operation that failed, for example <c>CreateSubscription</c>.</summary>
    public string Operation { get; }

    /// <summary>The HTTP status the provider returned, when the failure reached the provider.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Validation messages the provider returned. Never contains credentials.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}
