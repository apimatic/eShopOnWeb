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
/// Subscribes the calling (JWT-authenticated) user to a plan. Idempotently ensures a Maxio
/// customer exists for the user and enrolls them - repeated calls (e.g. a double-click) for
/// the same plan return the existing subscription rather than creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var (subscription, created) = await subscriptionService.SubscribeAsync(username, username, request.PlanHandle);

        response.Subscription = ToDto(subscription);
        response.AlreadyEnrolled = !created;

        return created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
