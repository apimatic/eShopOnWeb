using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotent by JWT identity) and enrolls them; a repeat request returns the existing subscription
/// rather than creating a duplicate. The caller's identity comes from the JWT, never the request body.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing) =>
            {
                request.UserReference = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest("A planHandle is required.");

        var subscription = await billing.SubscribeAsync(new SubscribeRequest
        {
            UserReference = request.UserReference,
            Email = request.UserReference, // eShopOnWeb usernames are email addresses
            PlanHandle = request.PlanHandle
        });

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        };
        return Results.Ok(response);
    }
}
