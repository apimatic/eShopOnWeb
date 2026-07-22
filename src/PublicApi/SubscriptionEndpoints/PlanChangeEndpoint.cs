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
/// Commit a plan change with the chosen timing (UC3 step 4). If the caller echoes back the preview
/// it was shown, the change is only applied while that quote still holds.
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
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangeResponse(request.CorrelationId());
        var timing = request.ResolveTiming();

        var subscription = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId,
            request.TargetPlanHandle,
            timing,
            ToDomainPreview(request, timing));

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }

    private static PlanChangePreview? ToDomainPreview(PlanChangeRequest request, PlanChangeTiming timing)
    {
        var confirmed = request.ConfirmedPreview;
        if (confirmed is null)
        {
            return null;
        }

        return new PlanChangePreview(request.SubscriptionId,
            confirmed.CurrentPlanHandle,
            request.TargetPlanHandle,
            timing,
            confirmed.ProratedAdjustmentInCents,
            confirmed.ChargeInCents,
            confirmed.PaymentDueInCents,
            confirmed.CreditAppliedInCents);
    }
}
