using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the caller
/// (idempotent) and enrolls them, returning the confirmed plan/price/state/next-billing-date.
/// The operation is idempotent: a double-click never creates a second customer or subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct)
                => await HandleAsync(request, user, billing, ct))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    // Satisfies IEndpoint; the route lambda calls the identity/cancellation-aware overload.
    public Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billing)
        => HandleAsync(request, principal: null, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal? principal,
        IMaxioBillingService billing, CancellationToken cancellationToken)
    {
        var user = BillingUserFactory.FromPrincipal(principal);
        if (user is null)
            return Results.Unauthorized();

        var result = await billing.SubscribeAsync(user, request.PlanHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadyExisted = result.AlreadyExisted
        };

        // 200 when the shopper was already enrolled (idempotent hit); 201 for a fresh subscription.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions/{response.Subscription!.SubscriptionId}", response);
    }
}
