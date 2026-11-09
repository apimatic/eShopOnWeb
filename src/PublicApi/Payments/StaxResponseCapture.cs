using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StaxFattMerchantApi.Core.Hooks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Some SDK operations (for example the invoice schedule ones) return no body to the caller.
/// This hook lets the caller read the body of the response the SDK received for the current call.
/// </summary>
public sealed class StaxResponseCapture
{
    private sealed class Holder
    {
        public string? Body;
    }

    private static readonly AsyncLocal<Holder?> Current = new();

    public SdkHook Hook { get; }

    public StaxResponseCapture()
    {
        Hook = SdkHook.OnResponse(async (HttpResponseMessage response, HookContext context, CancellationToken ct) =>
        {
            var holder = Current.Value;
            if (holder is null || !response.IsSuccessStatusCode)
            {
                return;
            }
            // Buffer so the SDK can still read/dispose the content afterwards.
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            holder.Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        });
    }

    /// <summary>Runs <paramref name="call"/> and returns the body of the successful response it received.</summary>
    public async Task<string?> CaptureAsync(System.Func<Task> call)
    {
        var holder = new Holder();
        Current.Value = holder;
        try
        {
            await call().ConfigureAwait(false);
            return holder.Body;
        }
        finally
        {
            Current.Value = null;
        }
    }
}
