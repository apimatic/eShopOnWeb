using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Subscription billing was invoked while its configuration is incomplete. Surfaced as 503 so an
/// operator can tell a misconfigured deployment apart from a provider outage.
/// </summary>
public class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException(IReadOnlyList<string> problems)
        : base($"Subscription billing is not configured: {string.Join("; ", problems)}")
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }
}
