using System;
using System.Security.Claims;
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
/// UC3 steps 3-5 — commits a plan change. <see cref="CommitPlanChangeRequest.ExpectedProratedAdjustmentInCents"/>
/// and <see cref="CommitPlanChangeRequest.ExpectedChargeInCents"/> must match the amounts most
/// recently previewed; a stale preview yields HTTP 409 with the fresh amounts (via <see cref="Middleware.ExceptionMiddleware"/>).
/// </summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CommitPlanChangeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, new SubscriptionEndpointContext(subscriptionService, user));
            })
            .Produces<CommitPlanChangeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, SubscriptionEndpointContext context)
    {
        if (!Enum.TryParse<PlanChangeTiming>(request.Timing, ignoreCase: true, out var timing))
        {
            return Results.BadRequest($"Invalid timing '{request.Timing}'. Expected 'Immediate' or 'AtNextRenewal'.");
        }

        var response = new CommitPlanChangeResponse(request.CorrelationId());
        var userReference = SubscriptionEndpointHelpers.RequireUserReference(context.User);

        var subscription = await context.SubscriptionService.CommitPlanChangeAsync(
            userReference,
            request.SubscriptionId,
            request.TargetProductHandle,
            timing,
            request.ExpectedProratedAdjustmentInCents,
            request.ExpectedChargeInCents);

        response.Subscription = SubscriptionDtoMapper.ToDto(subscription);
        return Results.Ok(response);
    }
}

public class CommitPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = nameof(PlanChangeTiming.Immediate);
    public int ExpectedProratedAdjustmentInCents { get; set; }
    public int ExpectedChargeInCents { get; set; }
}

public class CommitPlanChangeResponse : BaseResponse
{
    public CommitPlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CommitPlanChangeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
