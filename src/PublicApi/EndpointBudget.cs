using System;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// A single home for the whole-call timeout budget applied to endpoints that reach the SMS
/// provider. The Twilio SDK's per-attempt timeout is not a call budget; this bounds the whole
/// handler (send + any follow-up call) so a hung provider cannot hold a request open indefinitely.
/// </summary>
public static class EndpointBudget
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    public static CancellationTokenSource Start() => new(Default);
}
