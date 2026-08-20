using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioNotConfiguredException : Exception
{
    public MaxioNotConfiguredException()
        : base("Maxio Advanced Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle (from MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, and MAXIO_DEFAULT_PRODUCT_FAMILY).")
    {
    }
}
