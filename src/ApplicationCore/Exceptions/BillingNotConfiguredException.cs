using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException()
        : base("Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.")
    {
    }
}
