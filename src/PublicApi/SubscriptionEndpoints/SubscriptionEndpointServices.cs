using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Aggregates the services required by the subscription endpoints, since the
/// MinimalApi.Endpoint contract injects a single service.
/// </summary>
public class SubscriptionEndpointServices
{
    public UserManager<ApplicationUser> UserManager { get; }
    public ISubscriptionService SubscriptionService { get; }
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionEndpointServices(UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
    {
        UserManager = userManager;
        SubscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// The authenticated caller (from the JWT bearer token), or null.
    /// </summary>
    public ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;
}