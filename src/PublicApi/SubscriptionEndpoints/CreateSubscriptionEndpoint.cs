using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        IOptions<MaxioSettings> settings,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                request.UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return await HandleAsync(request);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userReference = request.UserId;
            if (string.IsNullOrEmpty(userReference))
            {
                _logger.LogWarning("User ID not found in JWT claims");
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "PlanHandle is required" });
            }

            int customerId = await GetOrCreateCustomer(userReference);

            var maxioRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.PlanHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(
                body: maxioRequest,
                ct: default);

            if (subscriptionResponse?.Subscription != null)
            {
                response.Subscription = new SubscriptionDto
                {
                    Id = subscriptionResponse.Subscription.Id,
                    State = subscriptionResponse.Subscription.State?.ToString(),
                    ProductPriceInCents = subscriptionResponse.Subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt = subscriptionResponse.Subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscriptionResponse.Subscription.NextAssessmentAt,
                    CreatedAt = subscriptionResponse.Subscription.CreatedAt,
                    UpdatedAt = subscriptionResponse.Subscription.UpdatedAt
                };
                response.Message = "Subscription created successfully";
            }

            return Results.Ok(response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Failed to create subscription in Maxio");

            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errorMsg = errorList?.ToString() ?? "Validation error";
                return Results.BadRequest(new { error = errorMsg });
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                return Results.StatusCode((int)rawError.StatusCode);
            }

            return Results.StatusCode(500);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error: HTTP {StatusCode}", (int)ex.Error.StatusCode);
            return Results.StatusCode((int)ex.Error.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            return Results.StatusCode(500);
        }
    }

    private async Task<int> GetOrCreateCustomer(string userReference)
    {
        try
        {
            var existingCustomer = await _maxioClient.Customers.ReadCustomerByReference(
                reference: userReference,
                ct: default);

            if (existingCustomer?.Customer?.Id.HasValue == true)
            {
                _logger.LogInformation("Found existing Maxio customer for reference {Reference}", userReference);
                return (int)existingCustomer.Customer.Id.Value;
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.LogError(ex, "Error reading customer by reference");
                throw;
            }
        }

        var createCustomerRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "Customer",
                LastName = userReference,
                Email = $"user-{userReference}@eshop.local",
                Reference = userReference
            }
        };

        var newCustomer = await _maxioClient.Customers.CreateCustomer(
            body: createCustomerRequest,
            ct: default);

        if (newCustomer?.Customer?.Id.HasValue == true)
        {
            _logger.LogInformation("Created new Maxio customer with reference {Reference}", userReference);
            return (int)newCustomer.Customer.Id.Value;
        }

        throw new InvalidOperationException("Failed to create or retrieve customer");
    }
}
