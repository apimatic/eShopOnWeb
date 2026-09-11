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

public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionListRequest, ClaimsPrincipal>
{
    private readonly IMaxioBillingService _svc;
    private readonly UserManager<ApplicationUser> _um;

    public MySubscriptionListEndpoint(IMaxioBillingService svc, UserManager<ApplicationUser> um)
    {
        _svc = svc;
        _um = um;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = "Bearer")]
            async (ClaimsPrincipal user) =>
            {
                return await HandleAsync(new MySubscriptionListRequest(), user);
            })
            .Produces<MySubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionListRequest request, ClaimsPrincipal user)
    {
        var username = user.FindFirst("unique_name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return Results.Unauthorized();

        var appUser = await _um.FindByNameAsync(username);
        if (appUser == null)
            return Results.Unauthorized();

        var subs = await _svc.GetMySubscriptionsAsync(appUser.Id);
        var resp = new MySubscriptionListResponse
        {
            Subscriptions = subs.Select(s => new MySubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                PlanName = s.PlanName,
                PlanHandle = s.PlanHandle,
                Price = $"{s.PriceInCents / 100.0:F2}",
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt
            }).ToList()
        };
        return Results.Ok(resp);
    }
}
