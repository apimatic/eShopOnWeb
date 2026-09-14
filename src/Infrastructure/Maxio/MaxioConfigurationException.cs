using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing integration is not configured (missing credentials).
/// </summary>
public class MaxioConfigurationException : InvalidOperationException
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
