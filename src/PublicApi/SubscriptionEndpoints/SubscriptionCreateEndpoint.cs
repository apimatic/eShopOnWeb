using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, Maxio.MaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, Maxio.MaxioClient maxioClient, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? httpContext.User.FindFirstValue("sub")
                    ?? throw new InvalidOperationException("Unable to determine user identity from JWT.");
                request.UserId = userId;
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, Maxio.MaxioClient maxioClient)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var customer = await maxioClient.EnsureCustomerAsync(
            request.FirstName,
            request.LastName,
            request.Email,
            request.UserId);

        var subscription = await maxioClient.CreateSubscriptionAsync(request.ProductHandle, customer.Id);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductId = subscription.ProductId,
            ProductHandle = subscription.ProductHandle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            CustomerId = subscription.CustomerId,
            PriceInCents = subscription.ProductPriceInCents,
            CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
            Currency = subscription.Currency ?? "USD",
            CreatedAt = subscription.CreatedAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt
        };

        return Results.Created($"api/my-subscriptions/{subscription.Id}", response);
    }
}
