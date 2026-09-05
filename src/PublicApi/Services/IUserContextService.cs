using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IUserContextService
{
    string? GetCurrentUserId();
    string? GetCurrentUserEmail();
}
