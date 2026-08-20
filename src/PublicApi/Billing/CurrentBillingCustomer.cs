using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface ICurrentBillingCustomer
{
    Task<BillingCustomer> GetAsync();
}

public sealed class CurrentBillingCustomer(
    IHttpContextAccessor httpContextAccessor,
    UserManager<ApplicationUser> userManager) : ICurrentBillingCustomer
{
    public async Task<BillingCustomer> GetAsync()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ??
                      principal?.Claims.FirstOrDefault(claim => claim.Type == "sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "A stable authenticated user identifier is required.");
        }

        var user = await userManager.FindByIdAsync(subject);
        if (user is null)
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "The authenticated user profile was not found.");
        }

        if (string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName) ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            throw new BillingValidationException(
                "The user profile must include first name, last name, and email before subscribing.");
        }

        return new BillingCustomer(user.Id, user.FirstName, user.LastName, user.Email);
    }
}
