using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Models.Subscription;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                async (HttpContext context, IMaxioSubscriptionService subscriptionService) =>
                    await HandleAsync(context, subscriptionService))
            .WithName("GetMySubscriptions")
            .Produces<ListUserSubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context, IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListUserSubscriptionsResponse();

        var userName = context.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var (customer, _) = await subscriptionService.GetOrCreateCustomerAsync(
                customerReference: userName,
                firstName: "Customer",
                lastName: userName,
                email: $"{userName}@eshop.local",
                ct: CancellationToken.None);

            if (customer?.Id == null)
            {
                return Results.Ok(response);
            }

            var subscriptions = await subscriptionService.ListCustomerSubscriptionsAsync(
                customerId: customer.Id.Value,
                ct: CancellationToken.None);

            foreach (var subscription in subscriptions)
            {
                if (subscription != null)
                {
                    var dto = new UserSubscriptionDto
                    {
                        SubscriptionId = subscription.Id ?? 0,
                        ProductName = subscription.Product?.Name ?? "Unknown",
                        State = subscription.State?.Value ?? "unknown",
                        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                        ActivatedAt = subscription.ActivatedAt
                    };
                    response.Subscriptions.Add(dto);
                }
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
