using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioCustomerService : IMaxioCustomerService
{
    private readonly Dictionary<string, int> _userToMaxioCustomerId = new();
    private readonly object _lock = new();

    public Task StoreMaxioCustomerMappingAsync(string userId, int maxioCustomerId)
    {
        lock (_lock)
        {
            _userToMaxioCustomerId[userId] = maxioCustomerId;
        }
        return Task.CompletedTask;
    }

    public Task<int?> GetMaxioCustomerIdAsync(string userId)
    {
        lock (_lock)
        {
            if (_userToMaxioCustomerId.TryGetValue(userId, out var customerId))
            {
                return Task.FromResult((int?)customerId);
            }
        }
        return Task.FromResult<int?>(null);
    }
}
