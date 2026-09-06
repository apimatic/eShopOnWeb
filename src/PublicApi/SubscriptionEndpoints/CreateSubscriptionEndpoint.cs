using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        var response = new CreateSubscriptionResponse();
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

        var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? userId;

        if (string.IsNullOrEmpty(request.PlanHandle))
        {
            return Results.BadRequest("Plan handle is required");
        }

        try
        {
            // Step 2a: Find or create customer with userId as reference
            Customer? customer = null;
            try
            {
                var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(
                    reference: userId, ct: default);
                customer = customerResponse.Customer;
                _logger.LogInformation("Found existing Maxio customer for userId {UserId}", userId);
            }
            catch (SdkException<RawError> ex)
            {
                if ((int?)ex.Error.StatusCode == 404)
                {
                    _logger.LogInformation("Customer not found, creating new Maxio customer for userId {UserId}", userId);

                    var createCustomerRequest = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = userId.Length > 50 ? userId[..50] : userId,
                            LastName = "User",
                            Email = userEmail,
                            Reference = userId
                        }
                    };

                    var createResponse = await _maxioClient.Customers.CreateCustomer(
                        body: createCustomerRequest, ct: default);
                    customer = createResponse.Customer;
                    _logger.LogInformation("Created new Maxio customer {CustomerId} for userId {UserId}",
                        customer?.Id, userId);
                }
                else
                {
                    _logger.LogError(ex, "Error reading customer from Maxio. Status: {Status}",
                        (int?)ex.Error.StatusCode);
                    return Results.StatusCode(500);
                }
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                _logger.LogError(ex, "Error creating customer in Maxio");
                if (ex.Error.TryGetCustomerErrorResponse1(out var errorBody))
                {
                    return Results.UnprocessableEntity(new { error = errorBody });
                }
                return Results.StatusCode(422);
            }

            if (customer?.Id == null)
            {
                _logger.LogError("Failed to get or create customer");
                return Results.StatusCode(500);
            }

            // Step 2b: Create subscription
            var subscriptionReference = $"{userId}-{request.PlanHandle}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var createSubscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = request.PlanHandle,
                    Reference = subscriptionReference
                }
            };

            SubscriptionResponse subscriptionResponse;
            try
            {
                subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(
                    body: createSubscriptionRequest, ct: default);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                _logger.LogError(ex, "Error creating subscription in Maxio");
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    return Results.UnprocessableEntity(new { errors = errorList });
                }
                return Results.StatusCode(422);
            }

            var subscription = subscriptionResponse.Subscription;
            response.Subscription = new SubscriptionDto
            {
                Id = subscription?.Id,
                State = subscription?.State?.Value,
                PriceInCents = subscription?.ProductPriceInCents,
                PlanName = subscription?.Product?.Name,
                PlanHandle = subscription?.Product?.Handle,
                NextBillingDate = subscription?.NextAssessmentAt
            };

            _logger.LogInformation("Successfully created subscription {SubscriptionId} for user {UserId}",
                subscription?.Id, userId);

            return Results.Ok(response);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error from Maxio response");
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            return Results.StatusCode(500);
        }
    }
}
