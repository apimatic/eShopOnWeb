using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionListEndpoint
{
    public static void MapRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService maxioService, HttpContext httpContext) =>
            {
                var response = new ListSubscriptionResponse();

                var userId = httpContext.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var reference = $"eshop-{userId}";
                var customer = await maxioService.FindCustomerByReferenceAsync(reference);

                if (customer == null)
                {
                    return Results.Ok(response);
                }

                var subscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.Id);

                response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    PlanName = s.Product?.Name ?? string.Empty,
                    PlanHandle = s.Product?.Handle ?? string.Empty,
                    Price = (s.Product?.PriceInCents ?? s.ProductPriceInCents) / 100m,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    ActivatedAt = s.ActivatedAt,
                    CanceledAt = s.CanceledAt
                }).ToList();

                return Results.Ok(response);
            })
            .RequireAuthorization()
            .Produces<ListSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
