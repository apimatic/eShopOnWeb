using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The configured metered component handle does not resolve to a component of metered kind on the product
/// family (UC0/UC2 precondition). Fix the sandbox seed before retrying - see plan.md UC0.
/// </summary>
public class MeteredComponentMisconfiguredException : Exception
{
    public MeteredComponentMisconfiguredException(string componentHandle)
        : base($"Metered component '{componentHandle}' is missing or is not of metered kind - usage cannot be recorded until the sandbox seed is corrected")
    {
    }
}
