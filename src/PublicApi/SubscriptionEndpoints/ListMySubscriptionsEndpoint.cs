using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the logged-in shopper (identified from the JWT).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () => await HandleAsync())
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListMySubscriptionsResponse();

        try
        {
            var shopper = await ResolveShopperAsync();
            if (shopper is null)
            {
                return Results.Unauthorized();
            }

            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(shopper, CurrentCancellationToken());
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointResult.From(ex);
        }
        catch (System.Exception ex)
        {
            return SubscriptionEndpointResult.FromUnexpected(ex, _logger, "listing the shopper's subscriptions");
        }
    }

    private async Task<MaxioShopper?> ResolveShopperAsync()
    {
        string? userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return null;
        }

        string email = string.IsNullOrWhiteSpace(applicationUser.Email) ? applicationUser.UserName ?? string.Empty : applicationUser.Email;
        return new MaxioShopper(applicationUser.Id, email);
    }

    private CancellationToken CurrentCancellationToken()
    {
        return _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
    }
}
