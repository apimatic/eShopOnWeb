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
/// Subscribes the authenticated buyer to a plan. Ensures a Maxio customer exists for the
/// buyer (idempotent) and enrolls them in the requested plan (idempotent) - a double
/// submission returns the buyer's existing enrollment rather than creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, subscriptionService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        // eShopOnWeb identities are keyed by username, which is also the account email
        // (see AppIdentityDbContextSeed) - so the JWT's Name claim doubles as both.
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());
        var enrollment = await subscriptionService.SubscribeAsync(buyerId, buyerId, request.PlanHandle, cancellationToken);

        response.Subscription = new SubscriptionDto
        {
            SubscriptionId = enrollment.SubscriptionId,
            PlanHandle = enrollment.PlanHandle,
            PlanName = enrollment.PlanName,
            Price = enrollment.Price,
            State = enrollment.State,
            NextBillingDate = enrollment.NextBillingDate,
            CreatedAt = enrollment.CreatedAt,
            AlreadyExisted = enrollment.AlreadyExisted
        };

        return Results.Ok(response);
    }
}
