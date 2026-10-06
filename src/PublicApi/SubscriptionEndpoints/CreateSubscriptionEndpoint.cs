using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan. Idempotent: a repeated
/// (double-click) request returns the existing subscription instead of
/// creating a duplicate, and the Maxio customer is provisioned on demand.
/// </summary>
public partial class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly ISubscriberResolver _subscriberResolver;

    public CreateSubscriptionEndpoint(ISubscriberResolver subscriberResolver)
    {
        _subscriberResolver = subscriberResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var subscriber = await _subscriberResolver.ResolveAsync();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        SubscriptionDetails subscription;
        try
        {
            subscription = await billingService.SubscribeAsync(subscriber, request.ProductHandle);
        }
        catch (MaxioPlanNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            // The billing system of record rejected the request; surface it
            // without leaking it as an unexpected server error.
            return Results.Problem(title: "Maxio billing request failed", detail: ex.Message, statusCode: 502);
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            AlreadySubscribed = subscription.AlreadySubscribed,
            Message = subscription.AlreadySubscribed
                ? "You are already subscribed to this plan."
                : "Subscription created.",
            Subscription = ToDto(subscription)
        };

        return subscription.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }

    private static SubscriptionDto ToDto(SubscriptionDetails subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price.ToString("0.00"),
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
    };
}
