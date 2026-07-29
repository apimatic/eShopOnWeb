using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The hero flow: subscribes the authenticated shopper to a plan. Ensures a single Maxio
/// customer exists for the user and enrolls them idempotently, so a double-click never creates
/// two customers or two subscriptions. The caller's identity comes from the JWT.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        CustomerSubscription subscription = await billingService.SubscribeAsync(subscriber, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = CustomerSubscriptionDto.FromDomain(subscription),
            AlreadyExisted = subscription.AlreadyExisted,
            Message = subscription.AlreadyExisted
                ? $"You are already subscribed to {subscription.PlanName} ({subscription.State}). Next billing {Format(subscription.NextBillingAt)}."
                : $"Subscribed to {subscription.PlanName} ({subscription.State}). Next billing {Format(subscription.NextBillingAt)}.",
        };

        return subscription.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }

    private static string Format(System.DateTimeOffset? when) =>
        when.HasValue ? when.Value.ToString("yyyy-MM-dd") : "n/a";
}
