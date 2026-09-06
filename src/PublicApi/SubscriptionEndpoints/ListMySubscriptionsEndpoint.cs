using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            HandleAsync)
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        IMaxioBillingService billingService,
        IRepository<MaxioSubscription> subscriptionRepository,
        HttpContext httpContext)
    {
        var response = new ListMySubscriptionsResponse();

        try
        {
            var user = httpContext.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var dbSubscriptions = await subscriptionRepository.ListAsync();
            var userSubs = dbSubscriptions
                .Where(s => s.UserId == userId)
                .ToList();

            foreach (var sub in userSubs)
            {
                var maxioSub = await billingService.GetSubscriptionAsync(sub.MaxioSubscriptionId);
                if (maxioSub != null)
                {
                    response.Subscriptions.Add(new UserSubscriptionDto
                    {
                        Id = maxioSub.Id,
                        ProductHandle = sub.ProductHandle,
                        State = maxioSub.State,
                        Price = maxioSub.CurrentPriceInCents.HasValue
                            ? maxioSub.CurrentPriceInCents.Value / 100m
                            : 0,
                        ActivatedAt = maxioSub.ActivatedAt,
                        NextBillingAt = maxioSub.NextBillingAt,
                        CancelledAt = maxioSub.CancelledAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        return Results.Ok(response);
    }
}
