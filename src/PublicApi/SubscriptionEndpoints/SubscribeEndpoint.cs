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
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>
{
    private readonly IMaxioBillingService _svc;
    private readonly UserManager<ApplicationUser> _um;

    public SubscribeEndpoint(IMaxioBillingService svc, UserManager<ApplicationUser> um)
    {
        _svc = svc;
        _um = um;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = "Bearer")]
            async (SubscribeRequest req, ClaimsPrincipal user) =>
            {
                return await HandleAsync(req, user);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user)
    {
        var username = user.FindFirst("unique_name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return Results.Unauthorized();

        var appUser = await _um.FindByNameAsync(username);
        var result = await _svc.SubscribeAsync(appUser.Id, appUser.Email ?? username, appUser.UserName ?? username, request.PlanHandle);
        var resp = new SubscribeResponse
        {
            SubscriptionId = result.Id,
            State = result.State,
            PlanName = result.PlanName,
            PlanHandle = result.PlanHandle,
            Price = $"{result.PriceInCents / 100.0:F2}",
            NextBillingDate = result.NextBillingDate
        };
        return Results.Ok(resp);
    }
}
