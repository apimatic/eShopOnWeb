using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Carries the HTTP status of the most recent PayPal response to the gateway's catch site. The SDK does not
/// expose the transport status on a thrown typed error, so an <c>SdkHook.OnResponse</c> hook records it here.
/// The holder object is created at the call site (<see cref="Begin"/>) so it flows *down* into the hook's
/// async context; the hook mutates the shared holder, which the call site then reads back.
/// </summary>
public static class PayPalResponseContext
{
    private sealed class Holder { public int? Status; }

    private static readonly AsyncLocal<Holder?> _holder = new();

    /// <summary>Start a fresh capture scope for the current async flow (call before an SDK operation).</summary>
    public static void Begin() => _holder.Value = new Holder();

    /// <summary>Record a response status (called from the SDK response hook).</summary>
    public static void Record(int statusCode)
    {
        var holder = _holder.Value;
        if (holder is not null) holder.Status = statusCode;
    }

    /// <summary>The last response status recorded in this async flow, if any.</summary>
    public static int? LastStatusCode => _holder.Value?.Status;
}
