using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface IMaxioApiClient
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(string productFamilyHandle);
    Task<int?> FindCustomerByReferenceAsync(string reference);
    Task<int> CreateCustomerAsync(string reference, string firstName, string lastName, string email);
    Task<UserSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForCustomerAsync(int customerId);
}
