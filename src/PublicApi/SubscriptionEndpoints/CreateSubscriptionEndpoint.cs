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
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for them
/// (idempotent) and enrolls them in the requested plan (idempotent per plan).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeToPlanBody body, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var customerEmail = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(new CreateSubscriptionRequest(body.PlanHandle, customerEmail), billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var plan = await billingService.GetPlanAsync(request.PlanHandle);
        if (plan is null)
        {
            return Results.NotFound($"No subscription plan found with handle '{request.PlanHandle}'.");
        }

        var (subscription, created) = await billingService.SubscribeAsync(request.CustomerEmail, request.CustomerEmail, plan.Handle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.FromDomain(subscription),
            Created = created
        };

        return created
            ? Results.Created($"api/my-subscriptions/{subscription.SubscriptionId}", response)
            : Results.Ok(response);
    }
}
