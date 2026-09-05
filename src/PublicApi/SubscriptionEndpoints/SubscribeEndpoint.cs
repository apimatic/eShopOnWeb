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
/// Subscribes the caller to a Maxio billing plan. Ensures a Maxio customer exists for the
/// caller (keyed off their email, idempotently via Maxio's unique customer reference) and
/// enrolls them in the requested plan. Idempotent: re-subscribing to a plan the caller
/// already has a non-canceled subscription for returns that subscription instead of creating
/// a duplicate, so a double-click never creates two customers/subscriptions.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IMaxioBillingClient maxio) =>
            {
                request.BuyerEmail = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, maxio);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingClient maxio)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BuyerEmail))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await maxio.SubscribeAsync(request.BuyerEmail, request.BuyerEmail, request.PlanHandle);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt
        };

        return Results.Ok(response);
    }
}
