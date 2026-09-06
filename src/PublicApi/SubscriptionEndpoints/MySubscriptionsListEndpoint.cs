using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsListEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MySubscriptionsListEndpoint> _logger;

    public MySubscriptionsListEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MySubscriptionsListEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
           .Produces<MySubscriptionsListResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new MySubscriptionsListResponse();
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext == null)
        {
            return Results.Unauthorized();
        }

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("No user ID found in JWT claims");
            return Results.Unauthorized();
        }

        try
        {
            // Find customer by reference
            Customer? customer = null;
            try
            {
                var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(
                    reference: userId, ct: default);
                customer = customerResponse.Customer;
            }
            catch (SdkException<RawError> ex)
            {
                if ((int?)ex.Error.StatusCode == 404)
                {
                    _logger.LogInformation("No customer found for userId {UserId}", userId);
                    return Results.Ok(response);
                }

                _logger.LogError(ex, "Error reading customer from Maxio. Status: {Status}",
                    (int?)ex.Error.StatusCode);
                return Results.StatusCode(500);
            }

            if (customer?.Id == null)
            {
                return Results.Ok(response);
            }

            // List customer subscriptions
            var subscriptions = await _maxioClient.Customers.ListCustomerSubscriptions(
                customerId: (int)customer.Id, ct: default);

            response.Subscriptions.AddRange(subscriptions
                .Select(sr => sr.Subscription)
                .Where(s => s != null)
                .Select(s => new SubscriptionDto
                {
                    Id = s?.Id,
                    State = s?.State?.Value,
                    PriceInCents = s?.ProductPriceInCents,
                    PlanName = s?.Product?.Name,
                    PlanHandle = s?.Product?.Handle,
                    NextBillingDate = s?.NextAssessmentAt
                }));

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for user {UserId}",
                response.Subscriptions.Count, userId);

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscriptions from Maxio. Status: {Status}",
                (int?)ex.Error.StatusCode);
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions");
            return Results.StatusCode(500);
        }
    }
}
