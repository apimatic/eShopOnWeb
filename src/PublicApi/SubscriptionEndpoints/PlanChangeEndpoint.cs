using System.Security.Claims;
using System.Threading;
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
/// Commit a plan change with the chosen timing (UC3, step 4).
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, HttpRequest httpRequest, ClaimsPrincipal user,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var request = PlanChangeRequest.From(await SubscriptionRequestBody.ReadAsync(httpRequest, cancellationToken));
                return await HandleAsync(subscriptionId, request, user, subscriptionService, cancellationToken);
            })
            .Accepts<PlanChangeRequest>("application/json")
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(0, request, new ClaimsPrincipal(), subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var timing = request.ResolveTiming();

        var result = await subscriptionService.ChangePlanAsync(user.ToSubscriptionActor(), subscriptionId,
            request.PlanHandle, timing, request.PreviewedPaymentDue, cancellationToken);

        return Results.Ok(new PlanChangeResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            PreviousPlanHandle = result.PreviousPlanHandle,
            NewPlanHandle = result.NewPlanHandle,
            Timing = result.Timing.ToString(),
            PaymentDue = result.AppliedPaymentDue,
            AmountDueInCents = SubscriptionMapper.ToCents(result.AppliedPaymentDue),
            PaymentDueInCents = SubscriptionMapper.ToCents(result.AppliedPaymentDue),
            EffectiveAt = result.EffectiveAt
        });
    }
}
