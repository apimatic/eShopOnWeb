using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a Maxio plan. Ensures a Maxio customer exists for the
/// user (idempotent) and enrolls them; a repeat call for the same plan returns the existing
/// subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        var buyerEmail = user.Identity?.Name;
        Guard.Against.NullOrEmpty(buyerEmail, nameof(buyerEmail));

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        // The buyer's own username/email doubles as the Maxio customer reference - it's the
        // stable, unique-per-user identifier already available from the JWT.
        var subscription = await subscriptionService.SubscribeAsync(buyerEmail, buyerEmail, request.PlanHandle);
        response.Subscription = SubscriptionDto.FromMaxioSubscription(subscription);

        return Results.Ok(response);
    }
}
