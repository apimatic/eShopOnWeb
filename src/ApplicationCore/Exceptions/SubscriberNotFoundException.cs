using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriberNotFoundException : Exception
{
    public SubscriberNotFoundException() : base("The authenticated user no longer exists.")
    {
    }
}
