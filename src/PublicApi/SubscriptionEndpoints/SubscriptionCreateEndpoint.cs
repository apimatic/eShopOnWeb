using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionCreateEndpoint
{
    public static void MapRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext) =>
            {
                var response = new CreateSubscriptionResponse();

                var userId = httpContext.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var email = userId;
                var firstName = "Subscriber";
                var lastName = userId;
                var reference = $"eshop-{userId}";

                var customer = await maxioService.FindOrCreateCustomerAsync(firstName, lastName, email, reference);

                var existingSubscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.Id);
                var existing = existingSubscriptions.FirstOrDefault(s =>
                    s.Product?.Handle == request.ProductHandle &&
                    s.State is "active" or "trialing" or "pending");

                if (existing != null)
                {
                    response.Subscription = MapToDto(existing);
                    response.Message = "Existing subscription found";
                    return Results.Ok(response);
                }

                var subscription = await maxioService.CreateSubscriptionAsync(request.ProductHandle, customer.Id);

                response.Subscription = MapToDto(subscription);
                response.Message = "Subscription created successfully";
                return Results.Ok(response);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static SubscriptionDto MapToDto(Maxio.Models.MaxioSubscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            PlanName = sub.Product?.Name ?? string.Empty,
            PlanHandle = sub.Product?.Handle ?? string.Empty,
            Price = (sub.Product?.PriceInCents ?? sub.ProductPriceInCents) / 100m,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CanceledAt = sub.CanceledAt
        };
    }
}
