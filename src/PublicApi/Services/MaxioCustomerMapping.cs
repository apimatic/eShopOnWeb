using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioCustomerMapping : IMaxioCustomerMapping
{
    private readonly Dictionary<string, int> _map = new();

    public int? GetCustomerId(string userId)
    {
        _map.TryGetValue(userId, out var id);
        return id == 0 ? null : id;
    }

    public void SetCustomerId(string userId, int customerId)
    {
        _map[userId] = customerId;
    }
}
