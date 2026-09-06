using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<SubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);
    Task<(int CustomerId, bool IsNew)> EnsureMaxioCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken cancellationToken = default);
    Task<UserSubscription> CreateSubscriptionAsync(int maxioCustomerId, int maxioProductId, CancellationToken cancellationToken = default);
    Task<UserSubscription[]> GetCustomerSubscriptionsAsync(int maxioCustomerId, CancellationToken cancellationToken = default);
}
