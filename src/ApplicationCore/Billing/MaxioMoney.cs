namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public static class MaxioMoney
{
    public static decimal FromCents(long? cents) => (cents ?? 0) / 100m;
}
