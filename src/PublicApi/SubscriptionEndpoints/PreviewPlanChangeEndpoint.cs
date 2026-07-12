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

/// <summary>UC3 step 2 — previews the cost of a plan change before it is committed.</summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PreviewPlanChangeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, new SubscriptionEndpointContext(subscriptionService, user));
            })
            .Produces<PreviewPlanChangeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, SubscriptionEndpointContext context)
    {
        if (!Enum.TryParse<PlanChangeTiming>(request.Timing, ignoreCase: true, out var timing))
        {
            return Results.BadRequest($"Invalid timing '{request.Timing}'. Expected 'Immediate' or 'AtNextRenewal'.");
        }

        var response = new PreviewPlanChangeResponse(request.CorrelationId());
        var userReference = SubscriptionEndpointHelpers.RequireUserReference(context.User);

        var preview = await context.SubscriptionService.PreviewPlanChangeAsync(
            userReference, request.SubscriptionId, request.TargetProductHandle, timing);

        response.Preview = SubscriptionDtoMapper.ToDto(preview);
        return Results.Ok(response);
    }
}

public class PreviewPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = nameof(PlanChangeTiming.Immediate);
}

public class PreviewPlanChangeResponse : BaseResponse
{
    public PreviewPlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PreviewPlanChangeResponse()
    {
    }

    public PlanChangePreviewDto? Preview { get; set; }
}
