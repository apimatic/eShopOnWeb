namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public static class Money
{
    public static decimal FromCents(long cents) => cents / 100m;
}
