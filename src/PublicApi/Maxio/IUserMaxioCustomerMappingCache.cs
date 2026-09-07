namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IUserMaxioCustomerMappingCache
{
    void Set(string userId, int customerId);
    bool TryGet(string userId, out int customerId);
}
