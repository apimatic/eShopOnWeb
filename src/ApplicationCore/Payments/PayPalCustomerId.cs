using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalCustomerId
{
    public static string FromBuyer(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("eshop:" + buyerId));
        return Convert.ToHexString(hash)[..22];
    }
}
