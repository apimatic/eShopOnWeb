using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected the request as invalid. Carries the provider's own messages so the
/// caller sees why (e.g. "No payment method was on file for the $299.00 balance").
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(IEnumerable<string> errors)
        : base(Describe(errors))
    {
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }

    private static string Describe(IEnumerable<string>? errors)
    {
        var joined = string.Join("; ", errors ?? Enumerable.Empty<string>());
        return string.IsNullOrWhiteSpace(joined)
            ? "The billing system rejected the request."
            : $"The billing system rejected the request: {joined}";
    }
}
