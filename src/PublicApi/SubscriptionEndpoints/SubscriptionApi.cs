using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Glue between the subscription endpoints and the service: resolves the caller identity from the token.
/// </summary>
public class SubscriptionApi
{
    private readonly ISubscriptionService _service;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _http;

    public SubscriptionApi(ISubscriptionService service, UserManager<ApplicationUser> userManager, IHttpContextAccessor http)
    {
        _service = service;
        _userManager = userManager;
        _http = http;
    }

    private HttpContext Context => _http.HttpContext ?? throw new SubscriptionException(500, "No active request.");

    public async Task<(IReadOnlyList<SubscriptionPlanDto> Plans, bool IsPartial)> ListPlansAsync()
    {
        var listing = await _service.ListPlansAsync(Context.RequestAborted);
        return (listing.Plans.Select(SubscriptionPlanDto.From).ToList(), listing.IsPartial);
    }

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(string? planHandle)
    {
        var who = await ResolveAsync();
        var result = await _service.SubscribeAsync(who, planHandle, Context.RequestAborted);
        return (SubscriptionDto.From(result.Subscription), result.Created);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMineAsync()
    {
        var who = await ResolveAsync();
        var list = await _service.GetSubscriptionsAsync(who, Context.RequestAborted);
        return list.Select(SubscriptionDto.From).ToList();
    }

    /// <summary>
    /// The token carries only the user name (ClaimTypes.Name); the stable user id comes from Identity.
    /// </summary>
    private async Task<SubscriberIdentity> ResolveAsync()
    {
        var name = Context.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SubscriptionException(401, "The token does not identify a user.");
        }
        var user = await _userManager.FindByNameAsync(name);
        if (user is null)
        {
            throw new SubscriptionException(401, "The token does not identify a known user.");
        }
        return new SubscriberIdentity(user.Id, user.Email, user.UserName);
    }
}
