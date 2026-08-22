using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalWriteScope
{
    private static readonly AsyncLocal<bool> Sent = new();

    public static bool AlreadySent => Sent.Value;

    public static void Begin() => Sent.Value = false;

    public static void MarkSent() => Sent.Value = true;
}

internal sealed class PayPalDuplicateSendException : Exception
{
    public PayPalDuplicateSendException()
        : base("A PayPal write was not resent after a transport failure.")
    {
    }
}

internal static class PayPalLastStatus
{
    private static readonly AsyncLocal<int?> Status = new();

    public static int? Value
    {
        get => Status.Value;
        set => Status.Value = value;
    }
}
