using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a configured handle does not resolve to the entity the integration expects —
/// typically a sandbox that was reseeded or never seeded (plan.md UC0).
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message)
        : base($"{message} Check the billing provider seed (plan.md UC0) and the Maxio configuration.")
    {
    }
}
