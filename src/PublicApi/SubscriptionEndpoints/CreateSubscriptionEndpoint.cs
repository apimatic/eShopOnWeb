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

public class CreateSubscriptionEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                async (SubscriptionEnrollmentRequest request, HttpContext context,
                    IMaxioSubscriptionService subscriptionService) =>
                    await HandleAsync(request, context, subscriptionService))
            .WithName("CreateSubscription")
            .Produces<SubscriptionEnrollmentResponse>(StatusCodes.Status201Created)
            .Accepts<SubscriptionEnrollmentRequest>("application/json")
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        SubscriptionEnrollmentRequest request, HttpContext context,
        IMaxioSubscriptionService subscriptionService)
    {
        var response = new SubscriptionEnrollmentResponse(request.CorrelationId());

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
                return Results.BadRequest(new { error = "Failed to create or retrieve customer" });
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                customerId: customer.Id.Value,
                productHandle: request.ProductHandle,
                subscriptionReference: request.Reference,
                ct: CancellationToken.None);

            response.SubscriptionId = subscription?.Id;
            response.State = subscription?.State?.Value ?? "unknown";
            response.NextBillingAt = subscription?.CurrentPeriodEndsAt;
            response.ActivatedAt = subscription?.ActivatedAt;

            return Results.Created($"api/subscriptions/{subscription?.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
