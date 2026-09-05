using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Ensures a Maxio customer exists for them (idempotent - a
/// double-click never creates two customers) and enrolls them in the requested plan (idempotent -
/// a repeat call for a plan they already hold returns the existing subscription instead of a new one).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequestBody body, ClaimsPrincipal user, IBillingService billingService) =>
            {
                var email = user.Identity!.Name!;
                return await HandleAsync(new CreateSubscriptionRequest(email, email, body.PlanHandle), billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await billingService.SubscribeAsync(request.CustomerReference, request.CustomerEmail, request.PlanHandle);

        response.Subscription = new SubscriptionDto
        {
            SubscriptionId = subscription.BillingSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.PriceInCents / 100m,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAtUtc,
            NextBillingAt = subscription.NextBillingAtUtc
        };

        return Results.Created($"api/my-subscriptions/{response.Subscription.SubscriptionId}", response);
    }
}
