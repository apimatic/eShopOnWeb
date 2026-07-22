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
/// Prices a plan change without committing it (UC3, step 2). The returned signature must be sent
/// back to the commit endpoint.
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlanChangePreviewEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var userReference = _httpContextAccessor.CurrentUserReference();
        if (userReference is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        if (!TryParseTiming(request.Timing, out var timing))
        {
            return Results.BadRequest($"Timing must be '{nameof(PlanChangeTiming.Immediate)}' or '{nameof(PlanChangeTiming.AtNextRenewal)}'.");
        }

        var preview = await subscriptionService.PreviewPlanChangeAsync(userReference,
            request.SubscriptionId,
            request.TargetPlanHandle,
            timing);

        return Results.Ok(new PlanChangePreviewResponse(request.CorrelationId())
        {
            Preview = preview.ToDto()
        });
    }

    internal static bool TryParseTiming(string? value, out PlanChangeTiming timing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            timing = PlanChangeTiming.Immediate;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out timing) && Enum.IsDefined(timing);
    }
}
