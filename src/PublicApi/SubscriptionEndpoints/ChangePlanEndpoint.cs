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

/// <summary>
/// Commits a previously previewed plan change, either now (prorated) or at the next renewal (UC3).
/// </summary>
public class ChangePlanEndpoint : IEndpoint<IResult, ChangePlanRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ChangePlanRequest body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new ChangePlanRequest(subscriptionId, body.TargetProductHandle, body.ApplyNow,
                    body.ProratedAdjustmentInCents, body.ChargeInCents, body.PaymentDueInCents, body.CreditAppliedInCents,
                    user.Identity!.Name!);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<ChangePlanResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ChangePlanRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ChangePlanResponse(request.CorrelationId());

        var subscription = request.ApplyNow
            ? await subscriptionService.ChangePlanNowAsync(request.UserReference, request.SubscriptionId, request.TargetProductHandle,
                new BillingPlanChangePreview(request.ProratedAdjustmentInCents, request.ChargeInCents, request.PaymentDueInCents, request.CreditAppliedInCents))
            : await subscriptionService.SchedulePlanChangeAsync(request.UserReference, request.SubscriptionId, request.TargetProductHandle);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };

        return Results.Ok(response);
    }
}
