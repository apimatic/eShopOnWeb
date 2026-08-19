using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Marks a write so <see cref="OnceOnlySendHandler"/> will not resend it on transport retry.
/// State lives in AsyncLocal so it survives the SDK retry pipeline's fresh HttpRequestMessage.
/// </summary>
internal static class WriteOnceScope
{
    private static readonly AsyncLocal<bool> Armed = new();
    private static readonly AsyncLocal<bool> Sent = new();

    public static IDisposable Arm()
    {
        Armed.Value = true;
        Sent.Value = false;
        return new Disposer();
    }

    public static bool IsArmed => Armed.Value;

    public static bool TryMarkSent()
    {
        if (Sent.Value)
        {
            return false;
        }

        Sent.Value = true;
        return true;
    }

    private sealed class Disposer : IDisposable
    {
        public void Dispose()
        {
            Armed.Value = false;
            Sent.Value = false;
        }
    }
}
