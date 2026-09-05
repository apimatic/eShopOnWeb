using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for them
/// (idempotent, keyed on their eShopOnWeb username) and enrolls them in the requested plan.
/// A repeated call for a plan the shopper is already actively subscribed to returns that
/// existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, user, billing);
            })
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser())
            .Produces<CreateSubscriptionResponse>()
            .ProducesValidationProblem()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." },
            });
        }

        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(username, username, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            SubscriptionId = result.Subscription.SubscriptionId,
            PlanHandle = result.Subscription.PlanHandle,
            PlanName = result.Subscription.PlanName,
            Price = result.Subscription.PriceInCents / 100m,
            State = result.Subscription.State,
            NextBillingDate = result.Subscription.NextAssessmentAt ?? result.Subscription.CurrentPeriodEndsAt,
            AlreadyEnrolled = result.AlreadyEnrolled,
        };

        return result.AlreadyEnrolled
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
