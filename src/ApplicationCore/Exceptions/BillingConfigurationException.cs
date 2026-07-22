using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider is reachable, but the catalog it holds does not match what this integration
/// is configured to use — a configured handle does not resolve, or resolves to the wrong kind of
/// entity. Correcting this means re-seeding the provider (plan.md UC0), not retrying the request.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message)
        : base($"{message} Verify the billing provider seed (plan.md UC0) and the Maxio configuration.")
    {
    }
}
