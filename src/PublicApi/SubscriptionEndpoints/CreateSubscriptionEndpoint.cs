using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionApiRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        ILogger<CreateSubscriptionEndpoint> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionApiRequest request) =>
                await HandleAsync(request))
            .Produces<CreateSubscriptionApiResponse>(StatusCodes.Status200OK)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithName("CreateSubscription")
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request)
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

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new { error = "ProductHandle is required" });
            }

            Customer? existingCustomer = null;
            try
            {
                var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(userId, ct: default);
                existingCustomer = customerResponse?.Customer;
            }
            catch (SdkException<RawError> ex)
            {
                if ((int)ex.Error.StatusCode != 404)
                {
                    _logger.LogError(ex, "Error looking up customer by reference");
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error looking up customer");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            int customerId;
            if (existingCustomer != null)
            {
                customerId = existingCustomer.Id ?? 0;
                if (customerId == 0)
                {
                    _logger.LogError("Customer found but ID is empty");
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            }
            else
            {
                try
                {
                    var createCustomerRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = request.FirstName ?? "User",
                            LastName = request.LastName ?? userId,
                            Email = request.Email ?? $"{userId}@localhost",
                            Reference = userId
                        }
                    };

                    var customerResponse = await _maxioClient.Customers.CreateCustomer(createCustomerRequest, ct: default);
                    customerId = customerResponse?.Customer?.Id ?? 0;

                    if (customerId == 0)
                    {
                        _logger.LogError("Customer created but ID is empty");
                        return Results.StatusCode(StatusCodes.Status500InternalServerError);
                    }
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    if (ex.Error.TryGetCustomerErrorResponse1(out var err422))
                    {
                        _logger.LogWarning("Validation error creating customer: {errors}", err422);
                        return Results.BadRequest(new { error = "Customer creation validation failed", details = err422 });
                    }
                    else if (ex.Error.TryGetRawError(out RawError statusRaw))
                    {
                        _logger.LogError("Error creating customer: HTTP {status}", (int)statusRaw.StatusCode);
                        return Results.StatusCode((int)statusRaw.StatusCode);
                    }
                    _logger.LogError(ex, "Unexpected error creating customer");
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "JSON deserialization error creating customer");
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            }

            try
            {
                var createSubRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = request.ProductHandle,
                        CustomerId = customerId,
                        Reference = userId
                    }
                };

                var subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(
                    createSubRequest, ct: default);

                var subscription = subscriptionResponse?.Subscription;
                if (subscription == null)
                {
                    _logger.LogError("Subscription created but response is empty");
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }

                var result = new CreateSubscriptionApiResponse
                {
                    SubscriptionId = subscription.Id,
                    State = subscription.State?.ToString(),
                    ProductPriceInCents = subscription.ProductPriceInCents,
                    NextBillingDate = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
                };

                return Results.Ok(result);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var err422))
                {
                    _logger.LogWarning("Validation error creating subscription: {errors}", err422);
                    return Results.BadRequest(new { error = "Subscription validation failed", details = err422 });
                }
                else if (ex.Error.TryGetRawError(out RawError statusRaw))
                {
                    _logger.LogError("Error creating subscription: HTTP {status}", (int)statusRaw.StatusCode);
                    return Results.StatusCode((int)statusRaw.StatusCode);
                }
                _logger.LogError(ex, "Unexpected error creating subscription");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error creating subscription");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in CreateSubscriptionEndpoint");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionApiRequest
{
    public string? ProductHandle { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

public class CreateSubscriptionApiResponse
{
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class ApiErrorResponse
{
    public string? Error { get; set; }
    public object? Details { get; set; }
}
