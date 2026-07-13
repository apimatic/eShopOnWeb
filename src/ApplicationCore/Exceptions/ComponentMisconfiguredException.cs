using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the configured metered-component handle does not resolve to a metered-kind
/// component on the billing provider's product family (UC2 precondition / UC0 misconfiguration).
/// </summary>
public class ComponentMisconfiguredException : Exception
{
    public ComponentMisconfiguredException(string componentHandle)
        : base($"Component '{componentHandle}' does not resolve to a metered component. Verify the seed (UC0) and configuration.")
    {
    }
}
