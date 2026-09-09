using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated user, as recorded in Maxio.
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var user = await ResolveUserAsync();
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName!;

        IReadOnlyList<SubscriptionDto> subscriptions;
        try
        {
            subscriptions = await subscriptionService.GetMySubscriptionsAsync(user.Id, email);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                statusCode: (int)HttpStatusCode.BadGateway,
                title: "Maxio subscription lookup failed.",
                detail: ex.Message);
        }

        var response = new GetMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions);

        return Results.Ok(response);
    }

    private async Task<ApplicationUser?> ResolveUserAsync()
    {
        var userName = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }
}
