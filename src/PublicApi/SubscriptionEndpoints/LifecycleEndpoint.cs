using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// The single lifecycle management surface — pause, resume, cancel (immediate or end-of-period) and
/// reactivate (UC4). A customer may act on their own subscription; an administrator on any.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId,
             LifecycleRequest request,
             ClaimsPrincipal user,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserReference = SubscriptionUser.ReferenceOf(user);
                request.IsAdministrator = SubscriptionUser.IsAdministrator(user);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var action = PlanChangeTimingParser.ParseAction(request.Action);
        var timing = PlanChangeTimingParser.ParseCancellationTiming(request.CancellationTiming);

        var previousStatus = await ReadPreviousStatusAsync(request, subscriptionService, cancellationToken);

        var subscription = request.IsAdministrator
            ? await subscriptionService.ApplyLifecycleActionForAnyCustomerAsync(request.SubscriptionId,
                action, timing, request.Reason, cancellationToken)
            : await subscriptionService.ApplyLifecycleActionAsync(request.UserReference, request.SubscriptionId,
                action, timing, request.Reason, cancellationToken);

        return Results.Ok(new LifecycleResponse(request.CorrelationId())
        {
            Action = action.ToString(),
            PreviousStatus = previousStatus,
            EffectiveAt = ResolveEffectiveAt(action, subscription),
            Subscription = SubscriptionDto.FromSubscription(subscription)
        });
    }

    /// <summary>
    /// Reads the pre-transition state for reporting. Administrators act across customers and the
    /// customer-scoped listing would not contain the subscription, so the state is only looked up on
    /// the customer path.
    /// </summary>
    private static async Task<string> ReadPreviousStatusAsync(LifecycleRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (request.IsAdministrator)
        {
            return SubscriptionStatus.Unknown.ToString();
        }

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(request.UserReference, cancellationToken);
        var existing = subscriptions.FirstOrDefault(s => s.Id == request.SubscriptionId);

        return (existing?.Status ?? SubscriptionStatus.Unknown).ToString();
    }

    /// <summary>
    /// The date the transition takes effect: an end-of-period cancel defers to the period boundary,
    /// every other transition is effective at once.
    /// </summary>
    private static DateTimeOffset? ResolveEffectiveAt(SubscriptionLifecycleAction action, CustomerSubscription subscription)
    {
        if (action != SubscriptionLifecycleAction.Cancel)
        {
            return null;
        }

        return subscription.DelayedCancelAt ?? subscription.CanceledAt;
    }
}
