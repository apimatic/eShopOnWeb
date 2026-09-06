using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public interface IUserMaxioCustomerMappingStore
{
    Task StoreAsync(string userId, int maxioCustomerId);
    Task<int?> GetMaxioCustomerIdAsync(string userId);
}

public class InMemoryUserMaxioCustomerMappingStore : IUserMaxioCustomerMappingStore
{
    private readonly Dictionary<string, int> _mappings = new();
    private readonly object _lock = new();

    public Task StoreAsync(string userId, int maxioCustomerId)
    {
        lock (_lock)
        {
            _mappings[userId] = maxioCustomerId;
        }
        return Task.CompletedTask;
    }

    public Task<int?> GetMaxioCustomerIdAsync(string userId)
    {
        lock (_lock)
        {
            if (_mappings.TryGetValue(userId, out var customerId))
            {
                return Task.FromResult((int?)customerId);
            }
            return Task.FromResult((int?)null);
        }
    }
}
