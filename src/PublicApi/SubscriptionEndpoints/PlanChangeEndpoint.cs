using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Commits a plan change (UC3, step 4). Supplying the previewed amount makes the commit reject
/// itself if the cost moved since the preview, so the customer is never charged an unseen amount.
/// </summary>
public class PlanChangeEndpoint : SubscriptionEndpointBase,
    IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public PlanChangeEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanChangeRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var userReference = ResolveUserReference(request.UserReference);
        if (userReference is null)
        {
            return Denied();
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        if (!TryParseTiming(request.Timing, out var timing))
        {
            return Results.BadRequest(
                $"Unknown timing '{request.Timing}'. Use 'Immediate' or 'AtNextRenewal'.");
        }

        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.ChangePlanAsync(userReference,
            request.TargetPlanHandle,
            timing,
            request.ExpectedPaymentDue);

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }

    private static bool TryParseTiming(string? timing, out PlanChangeTiming parsed)
    {
        if (string.IsNullOrWhiteSpace(timing))
        {
            parsed = PlanChangeTiming.Immediate;
            return true;
        }

        return Enum.TryParse(timing, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
    }
}
