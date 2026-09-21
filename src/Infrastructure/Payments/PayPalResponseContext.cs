using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Carries the HTTP status of the last PayPal response into the gateway's catch blocks. A typed
/// SDK error body carries no status, so an <c>SdkHook.OnResponse</c> records it here (per attempt)
/// and the gateway reads it back when translating an exception. Async-local so concurrent requests
/// don't clobber each other.
/// </summary>
internal static class PayPalResponseContext
{
    private static readonly AsyncLocal<int?> _status = new();

    public static int? LastStatusCode
    {
        get => _status.Value;
        set => _status.Value = value;
    }
}
