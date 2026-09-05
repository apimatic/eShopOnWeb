using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, low-level wrapper over the Maxio Advanced Billing HTTP API. Contains no business
/// logic beyond request/response shaping - orchestration (find-or-create, dedup) lives in
/// <see cref="MaxioSubscriptionService"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Looks up a customer by their external reference. Returns null if none exists.</summary>
    Task<MaxioCustomerModel?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomerModel> CreateCustomerAsync(MaxioCreateCustomerAttributes attributes, string uniquenessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProductModel>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);

    Task<MaxioSubscriptionModel> CreateSubscriptionAsync(MaxioCreateSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscriptionModel>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
