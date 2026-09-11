using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<JsonObject> FindOrCreateCustomerAsync(string reference, string email, string firstName = "", string lastName = "");
    Task<JsonArray> ListPlansAsync(string familyHandle);
    Task<JsonObject> CreateSubscriptionAsync(string productHandle, string customerReference);
    Task<JsonArray> ListCustomerSubscriptionsAsync(string customerReference);
}
