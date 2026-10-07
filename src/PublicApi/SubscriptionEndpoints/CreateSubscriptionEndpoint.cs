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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan. Idempotent: a repeated call
/// for the same user and plan returns the existing subscription instead of
/// creating a second one. Responds with the plan, price, state and next
/// billing date as recorded by the billing system.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, null, subscriptionService, CancellationToken.None);
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, ClaimsPrincipal? user, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var username = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "A planHandle is required." });
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(username, request.PlanHandle, cancellationToken);
            response.Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                Reference = subscription.Reference,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                ProductPriceInCents = subscription.ProductPriceInCents,
                ProductPrice = subscription.ProductPriceInCents / 100m,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }
}
