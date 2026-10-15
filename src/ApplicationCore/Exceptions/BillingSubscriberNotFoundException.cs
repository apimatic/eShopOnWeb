using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The authenticated user no longer has an account, so no billing customer can be created for it.</summary>
public class BillingSubscriberNotFoundException : Exception
{
    public BillingSubscriberNotFoundException()
        : base("No account was found for the authenticated user.")
    {
    }
}
