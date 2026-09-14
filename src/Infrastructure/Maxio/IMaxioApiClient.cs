using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level Maxio Advanced Billing API surface used by <see cref="MaxioSubscriptionService"/>.
/// Extracted purely so the orchestration logic (idempotent ensure-customer / ensure-subscription)
/// can be unit tested without a live HTTP dependency.
/// </summary>
public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForConfiguredFamilyAsync(CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken);

    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionWithoutPaymentMethodAsync(string customerReference, string productHandle, string subscriptionReference, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
