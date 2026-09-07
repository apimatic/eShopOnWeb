using System.Collections.Concurrent;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class InMemoryUserMaxioCustomerMappingCache : IUserMaxioCustomerMappingCache
{
    private readonly ConcurrentDictionary<string, int> _mapping = new();

    public void Set(string userId, int customerId)
    {
        _mapping[userId] = customerId;
    }

    public bool TryGet(string userId, out int customerId)
    {
        return _mapping.TryGetValue(userId, out customerId);
    }
}
