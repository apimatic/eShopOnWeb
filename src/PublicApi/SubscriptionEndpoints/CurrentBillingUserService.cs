using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ICurrentBillingUserService
{
    Task<BillingUser> GetAsync(CancellationToken cancellationToken);
}

internal sealed class CurrentBillingUserService : ICurrentBillingUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentBillingUserService(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<BillingUser> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name;
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "Authentication is required.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.UnprocessableEntity,
                "The authenticated account must have an email address before subscribing.");
        }

        var localPart = user.Email.Split('@', 2)[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.FirstOrDefault() ?? "eShop";
        var lastName = nameParts.Skip(1).FirstOrDefault() ?? "Customer";

        return new BillingUser(user.Id, user.Email, firstName, lastName);
    }
}
