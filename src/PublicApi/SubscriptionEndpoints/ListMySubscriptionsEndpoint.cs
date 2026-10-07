using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), subscriptionService, user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService)
    {
        return await HandleAsync(request, subscriptionService, null!);
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService, ClaimsPrincipal? principal)
    {
        var applicationUser = await ResolveUserAsync(principal);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(applicationUser.Id);
            var response = new ListMySubscriptionsResponse(request.CorrelationId());
            response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return MaxioErrorMapper.Map(ex);
        }
    }

    private async Task<ApplicationUser?> ResolveUserAsync(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }
        var name = principal.Identity!.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(name);
    }
}
