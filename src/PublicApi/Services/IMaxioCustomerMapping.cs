namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioCustomerMapping
{
    int? GetCustomerId(string userId);
    void SetCustomerId(string userId, int customerId);
}
