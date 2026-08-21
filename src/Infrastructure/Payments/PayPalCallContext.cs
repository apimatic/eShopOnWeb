using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalCallContext
{
    private static readonly AsyncLocal<int?> LastStatus = new();
    private static readonly AsyncLocal<int> WriteCount = new();
    private static readonly AsyncLocal<bool> GuardWrites = new();

    public static int? LastStatusCode
    {
        get => LastStatus.Value;
        set => LastStatus.Value = value;
    }

    public static void BeginWriteGuard()
    {
        GuardWrites.Value = true;
        WriteCount.Value = 0;
        LastStatus.Value = null;
    }

    public static void EndWriteGuard()
    {
        GuardWrites.Value = false;
        WriteCount.Value = 0;
    }

    public static void CountWriteOrThrow()
    {
        if (!GuardWrites.Value)
        {
            return;
        }

        var next = WriteCount.Value + 1;
        WriteCount.Value = next;
        if (next > 1)
        {
            throw new DuplicateWriteRefusedException();
        }
    }
}
