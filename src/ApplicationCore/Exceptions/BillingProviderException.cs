using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the billing integration raises. Its <see cref="Message"/> is always
/// caller-safe: provider and framework exception text is kept in <see cref="Exception.InnerException"/>
/// and never surfaced on the wire.
/// </summary>
public class BillingProviderException : Exception
{
    private static readonly IReadOnlyCollection<string> NoDetails = Array.Empty<string>();

    public BillingProviderException(string message, BillingFailure failure,
        int? providerStatusCode = null, IEnumerable<string>? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        ProviderStatusCode = providerStatusCode;
        Details = details is null
            ? NoDetails
            : details.Where(d => !string.IsNullOrWhiteSpace(d)).ToArray();
    }

    public BillingFailure Failure { get; }

    /// <summary>HTTP status the billing provider returned, when one was observed. Diagnostic only.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Validation messages from the provider, safe to echo back to the caller.</summary>
    public IReadOnlyCollection<string> Details { get; }

    /// <summary><see cref="Message"/> with any provider validation detail appended.</summary>
    public string ToCallerMessage() =>
        Details.Count == 0 ? Message : $"{Message} ({string.Join("; ", Details)})";
}
