using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment processor refused or could not serve a request. Carries enough of PayPal's own error
/// report for the application to decide what an operator should do next.
/// </summary>
public class PaymentProcessorException : Exception
{
    public PaymentProcessorException(string message, string? errorName = null, int? httpStatus = null,
        IReadOnlyList<string>? issues = null, string? debugId = null)
        : base(message)
    {
        ErrorName = errorName;
        HttpStatus = httpStatus;
        Issues = issues ?? Array.Empty<string>();
        DebugId = debugId;
    }

    public string? ErrorName { get; }
    public int? HttpStatus { get; }
    public IReadOnlyList<string> Issues { get; }
    public string? DebugId { get; }

    public bool HasIssue(string issue)
    {
        foreach (var candidate in Issues)
        {
            if (string.Equals(candidate, issue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
