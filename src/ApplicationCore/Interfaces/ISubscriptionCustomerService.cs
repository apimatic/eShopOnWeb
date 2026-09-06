using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionCustomerService
{
    Task<SubscriptionCustomer?> GetByUserIdAsync(string userId);
    Task<SubscriptionCustomer?> GetByMaxioCustomerIdAsync(int maxioCustomerId);
    Task<SubscriptionCustomer> AddAsync(string userId, int maxioCustomerId);
}
