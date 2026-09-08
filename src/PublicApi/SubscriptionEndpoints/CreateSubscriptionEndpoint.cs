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
/// Subscribes the authenticated shopper to a plan (POST /api/subscriptions).
/// Idempotent: subscribing again to a plan the shopper already has an active subscription for
/// returns the existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = GetUserName(user);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeToPlanRequest(request.UserName, request.PlanHandle, request.FirstName, request.LastName),
            CancellationToken.None);

        response.Subscription = Map(result.Subscription);
        response.AlreadySubscribed = !result.Created;

        return Results.Ok(response);
    }

    internal static SubscriptionDto Map(SubscriptionSummary subscription) =>
        new SubscriptionDto
        {
            Reference = subscription.Reference,
            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
            CreatedAt = subscription.CreatedAt
        };

    private static string GetUserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidSubscriptionRequestException(
                "The bearer token did not carry a user identity.");
        }

        return name;
    }
}
