using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The integration is misconfigured — a configured handle does not resolve, or the entity it
/// resolves to does not match the shape the integration expects. Points back at UC0 (seeding).
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
