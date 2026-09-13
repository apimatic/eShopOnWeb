using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, HttpContext http) =>
            {
                request.UserId = http.User.FindFirstValue(ClaimTypes.Name)
                    ?? http.User.FindFirst("sub")?.Value
                    ?? throw new InvalidOperationException("User identity not found in token.");
                request.Email = http.User.FindFirstValue(ClaimTypes.Email)
                    ?? http.User.FindFirstValue("email")
                    ?? $"{request.UserId}@placeholder.local";
                request.FirstName = http.User.FindFirstValue(ClaimTypes.GivenName)
                    ?? http.User.FindFirst("given_name")?.Value
                    ?? "User";
                request.LastName = http.User.FindFirstValue(ClaimTypes.Surname)
                    ?? http.User.FindFirst("family_name")?.Value
                    ?? request.UserId;

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var subscription = await subscriptionService.SubscribeAsync(
            request.UserId, request.Email, request.FirstName, request.LastName,
            request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductPriceInCents = subscription.ProductPriceInCents,
                ProductPriceInDollars = subscription.ProductPriceInCents / 100.0,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt,
                Currency = subscription.Currency,
                ProductName = subscription.Product?.Name,
                ProductHandle = subscription.Product?.Handle,
                CustomerId = subscription.Customer?.Id,
                CustomerEmail = subscription.Customer?.Email
            }
        };

        return Results.Created($"/api/my-subscriptions", response);
    }
}
