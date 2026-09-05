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
/// Subscribes the caller (identified from their JWT) to a plan. Idempotent: resubmitting for a
/// plan the caller already has a live subscription to (e.g. a double-click) returns that same
/// subscription rather than creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, IMaxioSubscriptionGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionGateway gateway) =>
            {
                return await HandleAsync(request, user, gateway);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionGateway gateway)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        // The JWT's only identity claim is the user's name, which in eShopOnWeb is their email
        // address (see IdentityTokenClaimService). It is used both as the buyer/customer
        // reference and as the customer's email when a Maxio customer must be created.
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await gateway.SubscribeAsync(buyerId, buyerId, request.PlanHandle);

        response.Subscription = new CustomerSubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.PriceAmount,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingAt,
            CreatedAt = subscription.CreatedAt
        };

        return Results.Ok(response);
    }
}
