using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Subscription;

public static class GetMySubscriptionsEndpoint
{
    public static void MapGetMySubscriptions(this WebApplication app)
    {
        app.MapGet("api/my-subscriptions",
            GetMySubscriptions)
            .WithName("GetMySubscriptions")
            .Produces<GetMySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetMySubscriptions(
        HttpContext httpContext,
        MaxioSubscriptionService subscriptionService)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(userId, email);
            var response = new GetMySubscriptionsResponse
            {
                Subscriptions = subscriptions.ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioServiceException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

public class GetMySubscriptionsResponse
{
    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
