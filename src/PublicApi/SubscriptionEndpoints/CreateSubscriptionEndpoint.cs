using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscription(this WebApplication app)
    {
        app.MapPost("api/subscriptions", CreateSubscription)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        IMaxioSubscriptionService subscriptionService,
        HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
            {
                return Results.Unauthorized();
            }

            var customerId = await subscriptionService.EnsureCustomerAsync(userId, email);
            var subscription = await subscriptionService.CreateSubscriptionAsync(customerId, request.ProductHandle);

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                MaxioCustomerId = subscription.CustomerId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = request.ProductHandle,
                PriceInCents = (decimal)subscription.PriceInCents,
                NextBillingAt = subscription.NextBillingAt,
                CreatedAt = subscription.CreatedAt,
                UpdatedAt = subscription.UpdatedAt
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}
