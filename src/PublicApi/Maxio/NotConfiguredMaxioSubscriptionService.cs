using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Registered when the Maxio configuration section is incomplete; surfaces a clear 503
/// instead of failing the host at startup so the rest of the API stays available.
/// </summary>
public sealed class NotConfiguredMaxioSubscriptionService : IMaxioSubscriptionService
{
    private static MaxioApiException NotConfigured() =>
        new(503, "Maxio billing is not configured. Supply the Maxio: configuration section (ApiKey, Subdomain, ProductFamilyHandle).");

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default) => throw NotConfigured();

    public Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken ct = default) => throw NotConfigured();

    public Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(string username, CancellationToken ct = default) => throw NotConfigured();
}