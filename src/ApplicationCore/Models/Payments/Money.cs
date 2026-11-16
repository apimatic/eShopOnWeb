using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public static class Money
{
    public static long ToCents(decimal amount) =>
        checked((long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    public static decimal FromCents(long cents) => cents / 100m;
}
