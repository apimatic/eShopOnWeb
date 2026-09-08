using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioNotConfiguredException : Exception
{
    public MaxioNotConfiguredException()
        : base("Maxio billing is not configured. Set the Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle settings before using subscription features.")
    {
    }
}
