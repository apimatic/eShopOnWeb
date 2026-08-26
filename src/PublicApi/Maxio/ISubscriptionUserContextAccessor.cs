using System.Security.Claims;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionUserContextAccessor
{
    Task<BillingCustomerContext> GetCurrentCustomerAsync(ClaimsPrincipal principal);
}
