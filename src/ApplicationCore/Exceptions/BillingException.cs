using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing system reports a domain-level error (e.g. an unknown plan, a
/// validation failure, or a customer conflict). Carries the messages returned by the
/// provider so callers can surface actionable detail without depending on vendor types.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
        Errors = new[] { message };
    }

    public BillingException(string message, IEnumerable<string> errors) : base(message)
    {
        Errors = errors?.ToArray() ?? new[] { message };
    }

    public BillingException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new[] { message };
    }

    /// <summary>The individual error messages reported by the billing provider, if any.</summary>
    public IReadOnlyCollection<string> Errors { get; }
}
