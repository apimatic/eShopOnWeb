using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioCustomerService
{
    Task StoreMaxioCustomerMappingAsync(string userId, int maxioCustomerId);
    Task<int?> GetMaxioCustomerIdAsync(string userId);
}
