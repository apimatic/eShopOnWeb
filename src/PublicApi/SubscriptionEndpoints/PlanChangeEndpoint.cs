using System.Linq;
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
/// Commits a plan change (UC3, step 4), refusing the commit if the previewed cost is no longer
/// current.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId,
             PlanChangeRequest request,
             ClaimsPrincipal user,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserReference = SubscriptionUser.ReferenceOf(user);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        var timing = PlanChangeTimingParser.ParseTiming(request.Timing);

        var before = await subscriptionService.ListSubscriptionsAsync(request.UserReference, cancellationToken);
        var previousPlanHandle = before.FirstOrDefault(s => s.Id == request.SubscriptionId)?.PlanHandle;

        var subscription = await subscriptionService.ChangePlanAsync(request.UserReference,
            request.SubscriptionId,
            request.TargetPlanHandle,
            timing,
            request.ConfirmedPaymentDueInCents,
            cancellationToken);

        return Results.Ok(new PlanChangeResponse(request.CorrelationId())
        {
            PreviousPlanHandle = previousPlanHandle,
            Subscription = SubscriptionDto.FromSubscription(subscription)
        });
    }
}
