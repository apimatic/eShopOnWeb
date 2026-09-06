using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected the request as invalid (HTTP 422). The messages come straight from
/// the billing system and are safe to show to the caller.
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(IEnumerable<string> errors)
        : base(Describe(errors))
    {
        Errors = errors.ToList().AsReadOnly();
    }

    public IReadOnlyList<string> Errors { get; } = Array.Empty<string>();

    private static string Describe(IEnumerable<string> errors)
    {
        var joined = string.Join("; ", errors);
        return string.IsNullOrWhiteSpace(joined)
            ? "The billing system rejected the request."
            : joined;
    }
}
