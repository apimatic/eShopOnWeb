using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioNotConfiguredException : Exception
{
    public MaxioNotConfiguredException()
        : base("Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.")
    {
    }
}
