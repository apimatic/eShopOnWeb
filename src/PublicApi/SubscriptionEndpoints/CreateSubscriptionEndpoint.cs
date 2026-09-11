using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext) =>
            {
                var user = httpContext.User;
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? user.FindFirstValue(ClaimTypes.Name)
                             ?? user.Identity?.Name;

                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var email = user.FindFirstValue(ClaimTypes.Email) ?? $"{userId}@eshop.local";
                var firstName = user.FindFirstValue("firstName") ?? "Subscriber";
                var lastName = user.FindFirstValue("lastName") ?? "User";

                var response = new CreateSubscriptionResponse(request.CorrelationId());

                var customer = await maxioService.FindOrCreateCustomerAsync(
                    reference: userId,
                    email: email,
                    firstName: firstName,
                    lastName: lastName);

                var subscription = await maxioService.CreateOrFindSubscriptionAsync(
                    customerId: customer.Id,
                    productHandle: request.ProductHandle);

                response.Subscription = new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    ProductName = subscription.ProductName,
                    ProductHandle = subscription.ProductHandle,
                    PricePerPeriod = subscription.ProductPriceInCents.HasValue
                        ? subscription.ProductPriceInCents.Value / 100.0m
                        : null,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CreatedAt = subscription.CreatedAt,
                    PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                };

                return Results.Created($"api/subscriptions/{subscription.Id}", response);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService service)
    {
        throw new System.NotSupportedException("Use AddRoute for this endpoint.");
    }
}
