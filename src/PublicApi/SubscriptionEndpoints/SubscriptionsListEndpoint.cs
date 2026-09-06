using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionsListEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                return await HandleRequestAsync(subscriptionService, userManager, httpContext);
            })
            .RequireAuthorization()
            .Produces<SubscriptionsListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithTags("SubscriptionEndpoints")
            .WithName("ListMySubscriptions");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleRequestAsync(
        MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Get or create customer (idempotent lookup)
            int customerId;
            try
            {
                customerId = await subscriptionService.GetOrCreateCustomerAsync(
                    userId: userId,
                    email: user.Email ?? string.Empty,
                    firstName: user.UserName ?? string.Empty,
                    lastName: string.Empty);
            }
            catch
            {
                // If customer doesn't exist, return empty list
                var emptyResponse = new SubscriptionsListResponse { Subscriptions = new() };
                return Results.Ok(emptyResponse);
            }

            // Get subscriptions for this customer
            var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(customerId);

            var response = new SubscriptionsListResponse { Subscriptions = subscriptions };
            return Results.Ok(response);
        }
        catch (MaxioException)
        {
            return Results.StatusCode(500);
        }
    }
}

public class SubscriptionsListResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
