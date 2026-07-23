using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing integration is misconfigured — a required setting is missing, or a configured handle does
/// not resolve to an entity at the provider. Points the operator back at the sandbox seed (plan.md UC0).
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The configured handle failed to resolve at the provider.</summary>
    public static BillingConfigurationException UnresolvedHandle(string kind, string handle) =>
        new($"The configured {kind} handle '{handle}' does not resolve at the billing provider. " +
            "Re-seed the provider sandbox or update the Maxio configuration to match it (see plan.md UC0).");
}
