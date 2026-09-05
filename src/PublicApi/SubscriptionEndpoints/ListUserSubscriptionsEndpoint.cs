using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListUserSubscriptionsEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        ILogger<ListUserSubscriptionsEndpoint> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async () => await HandleAsync())
            .Produces<ListUserSubscriptionsResponse>()
            .WithName("GetMySubscriptions")
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        try
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null)
            {
                _logger.LogError("HttpContext is null");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User?.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("User ID not found in claims");
                return Results.Unauthorized();
            }

            var response = new ListUserSubscriptionsResponse();

            try
            {
                var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(userId, ct: default);
                var customer = customerResponse?.Customer;

                if (customer?.Id == null || customer.Id == 0)
                {
                    return Results.Ok(response);
                }

                var subscriptions = await _maxioClient.Customers.ListCustomerSubscriptions(
                    customer.Id.Value, ct: default);

                if (subscriptions == null)
                {
                    return Results.Ok(response);
                }

                foreach (var subResponse in subscriptions)
                {
                    if (subResponse.Subscription != null)
                    {
                        var dto = new SubscriptionDto
                        {
                            Id = subResponse.Subscription.Id,
                            State = subResponse.Subscription.State?.ToString(),
                            ProductPriceInCents = subResponse.Subscription.ProductPriceInCents,
                            CurrentPeriodEndsAt = subResponse.Subscription.CurrentPeriodEndsAt,
                            NextAssessmentAt = subResponse.Subscription.NextAssessmentAt,
                            ActivatedAt = subResponse.Subscription.ActivatedAt,
                            ProductHandle = subResponse.Subscription.Product?.Handle,
                            ProductName = subResponse.Subscription.Product?.Name,
                            PaymentCollectionMethod = subResponse.Subscription.PaymentCollectionMethod?.ToString()
                        };
                        response.Subscriptions.Add(dto);
                    }
                }

                return Results.Ok(response);
            }
            catch (SdkException<RawError> ex)
            {
                if ((int)ex.Error.StatusCode == 404)
                {
                    return Results.Ok(response);
                }

                _logger.LogError(ex, "Error fetching customer subscriptions: HTTP {status}",
                    (int)ex.Error.StatusCode);
                return Results.StatusCode((int)ex.Error.StatusCode);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error fetching subscriptions");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ListUserSubscriptionsEndpoint");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListUserSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
