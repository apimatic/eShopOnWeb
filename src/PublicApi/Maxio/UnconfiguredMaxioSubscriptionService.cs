using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Used when the <c>Maxio</c> configuration section is absent or incomplete. Every
/// subscription-billing call fails fast with a clear 503 rather than a confusing
/// dependency-injection error, while the rest of the PublicApi keeps working.
/// </summary>
public sealed class UnconfiguredMaxioSubscriptionService : IMaxioSubscriptionService
{
    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct) =>
        throw NotConfigured();

    public Task<SubscribeToPlanResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken ct) =>
        throw NotConfigured();

    public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string customerReference, CancellationToken ct) =>
        throw NotConfigured();

    private static MaxioSubscriptionException NotConfigured() =>
        new MaxioSubscriptionException(
            StatusCodes.Status503ServiceUnavailable,
            "Subscription billing is not configured on this server.",
            "The Maxio configuration section is incomplete (ApiKey, ProductFamilyHandle and Subdomain or BaseUrl are required).");
}
