using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing integration is not configured well enough to serve a request — for example no API
/// key or no site is bound. Surfaced as a 503 so that it reads as "capability unavailable" rather
/// than "your request was wrong".
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(IEnumerable<string> problems)
        : base("The subscription billing integration is not configured: " + string.Join("; ", problems))
    {
        Problems = problems.ToList().AsReadOnly();
    }

    public BillingConfigurationException(string problem) : this(new[] { problem })
    {
    }

    public IReadOnlyList<string> Problems { get; } = Array.Empty<string>();
}
