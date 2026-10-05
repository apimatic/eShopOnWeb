using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ShopperNotFoundException : Exception
{
    public ShopperNotFoundException()
        : base("No shopper account was found for the authenticated user.")
    {
    }
}
