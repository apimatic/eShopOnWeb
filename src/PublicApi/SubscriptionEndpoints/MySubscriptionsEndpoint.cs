using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsEndpoint.Dependencies>
{
    public sealed class Dependencies
    {
        public Dependencies(MaxioAdvancedBillingClient maxioClient, HttpContext httpContext, ILogger<MySubscriptionsEndpoint> logger)
        {
            MaxioClient = maxioClient;
            HttpContext = httpContext;
            Logger = logger;
        }

        public MaxioAdvancedBillingClient MaxioClient { get; }
        public HttpContext HttpContext { get; }
        public ILogger<MySubscriptionsEndpoint> Logger { get; }
    }

    public sealed class ListSubscriptionsResponse
    {
        public List<SubscriptionDto> Subscriptions { get; set; } = new();
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (Dependencies deps) =>
            {
                return await HandleAsync(deps);
            })
           .RequireAuthorization()
           .Produces<ListSubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(Dependencies deps)
    {
        try
        {
            var userIdClaim = deps.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim?.Value == null)
            {
                return Results.Unauthorized();
            }

            var userId = userIdClaim.Value;
            deps.Logger.LogInformation("Fetching subscriptions for user {UserId}", userId);

            var customer = await GetCustomerByReference(deps, userId);
            if (customer == null)
            {
                deps.Logger.LogInformation("No customer found for user {UserId}", userId);
                return Results.Ok(new ListSubscriptionsResponse());
            }

            var customerId = customer.Id ?? 0;
            var subscriptions = await deps.MaxioClient.Customers.ListCustomerSubscriptions(customerId: customerId, ct: default);

            var response = new ListSubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions
                .Where(sr => sr.Subscription != null)
                .Select(sr => new SubscriptionDto
                {
                    Id = sr.Subscription!.Id ?? 0,
                    State = sr.Subscription!.State?.ToString(),
                    CurrentPeriodEndsAt = sr.Subscription!.CurrentPeriodEndsAt,
                    NextAssessmentAt = sr.Subscription!.NextAssessmentAt,
                    ActivatedAt = sr.Subscription!.ActivatedAt,
                    ProductHandle = sr.Subscription!.Product?.Handle,
                    ProductName = sr.Subscription!.Product?.Name,
                    ProductPriceInCents = sr.Subscription!.Product?.PriceInCents
                }));

            deps.Logger.LogInformation("Successfully fetched {Count} subscriptions for user {UserId}", response.Subscriptions.Count, userId);
            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            deps.Logger.LogError(ex, "Maxio API error: {Status}", ex.Error.StatusCode);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            deps.Logger.LogError(ex, "Error fetching user subscriptions");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> GetCustomerByReference(Dependencies deps, string userId)
    {
        try
        {
            var response = await deps.MaxioClient.Customers.ReadCustomerByReference(reference: userId, ct: default);
            return response?.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            deps.Logger.LogError(ex, "Error fetching customer");
            return null;
        }
    }
}
