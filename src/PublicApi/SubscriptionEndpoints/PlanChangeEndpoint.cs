using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Commits a plan change, immediately with proration or at next renewal without it (UC3).</summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionService _subscriptionService;

    public PlanChangeEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ClaimsPrincipal user)
    {
        Guard.Against.Null(user.Identity?.Name, nameof(user.Identity.Name));

        var subscription = await _subscriptionService.CommitPlanChangeAsync(
            user.Identity!.Name!, request.SubscriptionId, request.TargetProductHandle, request.ApplyNow);

        var response = new PlanChangeResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        };
        return Results.Ok(response);
    }
}
