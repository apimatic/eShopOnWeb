using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Records the HTTP status of the most recent PayPal response on the current async flow, so the
/// error boundary can still map a failure to the provider's status when the SDK discards it
/// (e.g. an error body that does not match the generated error model).
/// </summary>
public static class PayPalResponseStatusTracker
{
    private static readonly AsyncLocal<int?> _lastStatus = new AsyncLocal<int?>();

    public static int? LastStatus
    {
        get => _lastStatus.Value;
        set => _lastStatus.Value = value;
    }
}
