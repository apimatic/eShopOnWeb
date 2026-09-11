using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface ISubscriptionMappingService
{
    string? GetCustomerReference(string userId);
    void SetCustomerReference(string userId, string reference);
    string? GetSubscriptionId(string userId);
    void SetSubscriptionId(string userId, string subscriptionId);
}

public class InMemorySubscriptionMappingService : ISubscriptionMappingService
{
    private readonly Dictionary<string, string> _customerRefs = new();
    private readonly Dictionary<string, string> _subscriptionIds = new();

    public string? GetCustomerReference(string userId) => _customerRefs.TryGetValue(userId, out var r) ? r : null;
    public void SetCustomerReference(string userId, string reference) => _customerRefs[userId] = reference;
    public string? GetSubscriptionId(string userId) => _subscriptionIds.TryGetValue(userId, out var s) ? s : null;
    public void SetSubscriptionId(string userId, string subscriptionId) => _subscriptionIds[userId] = subscriptionId;
}
