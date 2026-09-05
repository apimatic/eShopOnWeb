using System.Collections.Generic;
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
/// (idempotent - a double-click never creates two customers or two subscriptions) and enrolls
/// them in the requested plan, no payment method required.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, subscriptionService, user);
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesValidationProblem()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." }
            });
        }

        var email = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(email, email, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                MaxioSubscriptionId = subscription.Id,
                PlanHandle = subscription.ProductHandle,
                PlanName = subscription.ProductName,
                Price = subscription.ProductPriceInCents.HasValue ? subscription.ProductPriceInCents.Value / 100m : null,
                State = subscription.State,
                NextBillingDate = subscription.NextAssessmentAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            }
        };

        return Results.Ok(response);
    }
}
