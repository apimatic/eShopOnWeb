using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    private readonly IOptions<MaxioOptions> _options;

    public CreateSubscriptionEndpoint(IOptions<MaxioOptions> options)
    {
        _options = options;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequestBody body, IMaxioClient maxioClient, HttpContext httpContext) =>
            {
                var userName = httpContext.User.Identity?.Name ?? "unknown";
                var userEmail = httpContext.User.FindFirstValue(ClaimTypes.Email)
                    ?? (userName.Contains('@') ? userName : $"{userName}@eshop.local");

                var request = new CreateSubscriptionRequest
                {
                    ProductHandle = body.ProductHandle,
                    CustomerReference = body.CustomerReference ?? $"eshop-{userName}",
                    CustomerFirstName = body.CustomerFirstName ?? userName.Split('@')[0],
                    CustomerLastName = body.CustomerLastName ?? "User",
                    CustomerEmail = body.CustomerEmail ?? userEmail,
                    SubscriptionReference = body.SubscriptionReference
                };

                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            response.IsSuccess = false;
            response.ErrorMessage = "Product handle is required.";
            return Results.BadRequest(response);
        }

        try
        {
            var customer = await maxioClient.FindCustomerByReferenceAsync(request.CustomerReference!);
            if (customer is null)
            {
                customer = await maxioClient.CreateCustomerAsync(
                    request.CustomerFirstName!,
                    request.CustomerLastName!,
                    request.CustomerEmail!,
                    request.CustomerReference);
            }

            // Idempotency: check for existing active subscription on same product
            var existingSubscriptions = await maxioClient.GetCustomerSubscriptionsAsync(customer.Id);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                s.Product?.Handle == request.ProductHandle &&
                s.State is "active" or "trialing" or "past_due" or "on_hold");

            if (existing is not null)
            {
                response.IsSuccess = true;
                response.Subscription = MapToDto(existing);
                return Results.Ok(response);
            }

            var subscription = await maxioClient.CreateSubscriptionAsync(
                request.ProductHandle!,
                customer.Id,
                request.SubscriptionReference);

            response.IsSuccess = true;
            response.Subscription = MapToDto(subscription);
        }
        catch (MaxioApiException ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Maxio API error ({ex.StatusCode}): {ex.ResponseBody}";
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Failed to create subscription: {ex.Message}";
        }

        return Results.Ok(response);
    }

    private static SubscriptionDto MapToDto(Maxio.Models.Subscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            BalanceInCents = sub.BalanceInCents,
            TotalRevenueInCents = sub.TotalRevenueInCents,
            ProductPriceInCents = sub.ProductPriceInCents,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            CancelAtEndOfPeriod = sub.CancelAtEndOfPeriod,
            PaymentCollectionMethod = sub.PaymentCollectionMethod,
            Currency = sub.Currency,
            PlanName = sub.Product?.Name,
            PlanHandle = sub.Product?.Handle
        };
    }
}
