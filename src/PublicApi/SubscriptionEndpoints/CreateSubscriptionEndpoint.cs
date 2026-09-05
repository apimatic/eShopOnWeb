using System.Security.Claims;
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
/// Subscribes the caller to a plan. Ensures a Maxio customer exists for the caller and
/// enrolls them; idempotent, so retrying/double-clicking never creates a duplicate customer
/// or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                request.Username = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            // The eShopOnWeb username is the user's email, so it doubles as the Maxio
            // customer reference (a stable, unique per-user key) and contact email.
            var subscription = await billingService.SubscribeAsync(request.Username, request.Username, request.PlanHandle);
            response.Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                State = subscription.State,
                NextBillingAt = subscription.NextBillingAt
            };

            return subscription.IsNewlyCreated
                ? Results.Created($"api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
