using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionsCreateEndpoint : IEndpoint<IResult, SubscriptionsCreateEndpoint.CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, MaxioSubscriptionService subscriptionService,
                   UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                return await HandleRequestAsync(request, subscriptionService, userManager, httpContext);
            })
            .RequireAuthorization()
            .Produces<SubscriptionsCreateResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleRequestAsync(
        CreateSubscriptionRequest request,
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrEmpty(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "PlanHandle is required" });
        }

        try
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Get or create Maxio customer (idempotent)
            var customerId = await subscriptionService.GetOrCreateCustomerAsync(
                userId: userId,
                email: user.Email ?? string.Empty,
                firstName: user.UserName ?? string.Empty,
                lastName: string.Empty);

            // Create subscription
            var subscription = await subscriptionService.CreateSubscriptionAsync(
                planHandle: request.PlanHandle,
                customerId: userId);

            var response = new SubscriptionsCreateResponse
            {
                SubscriptionId = subscription.Id,
                Reference = subscription.Reference,
                State = subscription.State,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (MaxioException)
        {
            return Results.StatusCode(500);
        }
    }

    public class CreateSubscriptionRequest
    {
        public string PlanHandle { get; set; } = string.Empty;
    }
}

public class SubscriptionsCreateResponse
{
    public int SubscriptionId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
