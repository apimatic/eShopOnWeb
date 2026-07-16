using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC3 step 1-2: preview a plan change and receive a signed, time-limited preview token.</summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PreviewPlanChangeRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.CustomerReference = principal.Identity!.Name!;
                request.IsAdmin = principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());

        response.Preview = await subscriptionService.PreviewPlanChangeAsync(
            request.CustomerReference, request.SubscriptionId, request.TargetPlanHandle, request.ApplyNow, request.IsAdmin);

        return Results.Ok(response);
    }
}

/// <summary>UC3 step 3-4: commit a previously previewed plan change.</summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CommitPlanChangeRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                request.CustomerReference = principal.Identity!.Name!;
                request.IsAdmin = principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CommitPlanChangeResponse(request.CorrelationId());

        response.Subscription = await subscriptionService.CommitPlanChangeAsync(
            request.CustomerReference, request.PreviewToken, request.IsAdmin);

        return Results.Ok(response);
    }
}
