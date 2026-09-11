using System.Collections.Concurrent;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
public class SubscriptionMappingService
{
    private readonly ConcurrentDictionary<string, string> _userToCustomer = new();
    private readonly ConcurrentDictionary<string, string> _userToSubscription = new();
    public string? GetCustomerId(string userId) => _userToCustomer.TryGetValue(userId, out var c) ? c : null;
    public string? GetSubscriptionId(string userId) => _userToSubscription.TryGetValue(userId, out var s) ? s : null;
    public void SetMapping(string userId, string customerId, string subscriptionId)
    {
        _userToCustomer[userId] = customerId;
        _userToSubscription[userId] = subscriptionId;
    }
}
