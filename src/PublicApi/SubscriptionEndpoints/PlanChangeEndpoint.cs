using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
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
/// Preview and commit a plan change with proration (UC3). The preview route quotes the cost;
/// the commit route refuses a quote that has gone stale.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public PlanChangeEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerBuyerId = SubscriptionCaller.ResolveOwnerBuyerId(user);

                return await HandlePreviewAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerBuyerId = SubscriptionCaller.ResolveOwnerBuyerId(user);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandlePreviewAsync(PlanChangeRequest request,
        ISubscriptionService subscriptionService)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.SubscriptionId,
            request.OwnerBuyerId, request.TargetPlanHandle, request.Timing);

        response.Preview = _mapper.Map<PlanChangePreviewDto>(preview);

        return Results.Ok(response);
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request,
        ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());

        // Only re-validate against a fresh quote when the caller actually confirmed one.
        PlanChangePreview? confirmed = request.ConfirmedPreview is null
            ? null
            : new PlanChangePreview(request.TargetPlanHandle, request.Timing,
                request.ConfirmedPreview.ProratedAdjustmentInCents,
                request.ConfirmedPreview.ChargeInCents,
                request.ConfirmedPreview.PaymentDueInCents,
                request.ConfirmedPreview.CreditAppliedInCents);

        var subscription = await subscriptionService.ChangePlanAsync(request.SubscriptionId,
            request.OwnerBuyerId, request.TargetPlanHandle, request.Timing, confirmed);

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
