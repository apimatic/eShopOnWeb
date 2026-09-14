using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin wrapper over the raw Maxio Advanced Billing REST API (https://developers.maxio.com).
/// Not exposed outside Infrastructure — <see cref="IMaxioSubscriptionService"/> is the app-facing contract.
/// </summary>
internal interface IMaxioApiClient
{
    Task<MaxioCustomerWire?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomerWire> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken);

    Task<MaxioProductWire?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProductWire>> ListProductsForFamilyAsync(CancellationToken cancellationToken);

    Task<MaxioSubscriptionWire> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscriptionWire>> ListSubscriptionsForCustomerAsync(int maxioCustomerId, CancellationToken cancellationToken);
}
