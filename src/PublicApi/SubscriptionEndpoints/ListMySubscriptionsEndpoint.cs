using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListSubscriptionsRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user) =>
            {
                var request = new ListSubscriptionsRequest(user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return await HandleAsync(request);
            })
            .RequireAuthorization()
            .Produces<ListSubscriptionsResponse>();
    }

    public async Task<IResult> HandleAsync(ListSubscriptionsRequest request)
    {
        var response = new ListSubscriptionsResponse(request.CorrelationId());

        try
        {
            var userReference = request.UserId;
            if (string.IsNullOrEmpty(userReference))
            {
                _logger.LogWarning("User ID not found in JWT claims");
                return Results.Unauthorized();
            }

            int customerId;
            try
            {
                var customer = await _maxioClient.Customers.ReadCustomerByReference(
                    reference: userReference,
                    ct: default);

                if (customer?.Customer?.Id is not > 0)
                {
                    return Results.Ok(response);
                }

                customerId = (int)customer.Customer.Id!;
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    return Results.Ok(response);
                }

                _logger.LogError(ex, "Error reading customer by reference");
                return Results.StatusCode((int)ex.Error.StatusCode);
            }

            var subscriptions = await _maxioClient.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: default);

            if (subscriptions != null)
            {
                foreach (var sub in subscriptions)
                {
                    if (sub.Subscription != null)
                    {
                        response.Subscriptions.Add(new SubscriptionDto
                        {
                            Id = sub.Subscription.Id,
                            State = sub.Subscription.State?.ToString(),
                            ProductPriceInCents = sub.Subscription.ProductPriceInCents,
                            CurrentPeriodEndsAt = sub.Subscription.CurrentPeriodEndsAt,
                            NextAssessmentAt = sub.Subscription.NextAssessmentAt,
                            CreatedAt = sub.Subscription.CreatedAt,
                            UpdatedAt = sub.Subscription.UpdatedAt
                        });
                    }
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error: HTTP {StatusCode}", (int)ex.Error.StatusCode);
            return Results.StatusCode((int)ex.Error.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions");
            return Results.StatusCode(500);
        }
    }
}
