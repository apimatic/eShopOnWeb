using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Resolves the authenticated eShopOnWeb user for the current request.
/// </summary>
public interface ICurrentUserContext
{
    Task<ApplicationUser?> GetCurrentUserAsync();
}
