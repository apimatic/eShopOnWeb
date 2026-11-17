using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class Money
{
    public static long ToCents(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
