using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public interface ICurrentShopperService
{
    Task<MaxioShopper?> GetCurrentShopperAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
