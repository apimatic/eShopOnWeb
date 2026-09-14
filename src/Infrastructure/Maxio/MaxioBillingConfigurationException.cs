using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The Maxio billing integration is not configured correctly (missing/invalid settings).
/// </summary>
public sealed class MaxioBillingConfigurationException : Exception
{
    public MaxioBillingConfigurationException(string message)
        : base(message)
    {
    }
}
