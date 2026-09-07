using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionDto>
    .WithActionResult<SubscriptionEnrollmentResponse>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(MaxioAdvancedBillingClient maxioClient, IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Enrolls the logged-in user in a subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionEnrollmentResponse>> HandleAsync(
        CreateSubscriptionDto request,
        CancellationToken cancellationToken = default)
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "User identity not found" });
        }

        try
        {
            var customerReference = userId;

            var existingCustomer = await TryGetCustomer(customerReference, cancellationToken);
            int customerId;

            if (existingCustomer != null)
            {
                customerId = existingCustomer.Id ?? 0;
            }
            else
            {
                var customerResponse = await CreateCustomer(userId, customerReference, cancellationToken);
                customerId = customerResponse?.Id ?? 0;
            }

            var subscriptionReference = $"{userId}-{request.ProductHandle}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var createSubRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = request.ProductHandle,
                    Reference = subscriptionReference
                }
            };

            var subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(
                createSubRequest,
                ct: cancellationToken);

            if (subscriptionResponse?.Subscription == null)
            {
                return BadRequest(new { error = "Failed to create subscription" });
            }

            return new SubscriptionEnrollmentResponse
            {
                Id = subscriptionResponse.Subscription.Id ?? 0,
                CustomerId = customerId,
                State = subscriptionResponse.Subscription.State?.ToString() ?? "Unknown",
                ProductPriceInCents = subscriptionResponse.Subscription.ProductPriceInCents ?? 0,
                NextBillingDate = subscriptionResponse.Subscription.NextAssessmentAt,
                CurrentPeriodEndsAt = subscriptionResponse.Subscription.CurrentPeriodEndsAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            return HandleSubscriptionError(ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            return HandleCustomerError(ex);
        }
        catch (SdkException<RawError> ex)
        {
            return StatusCode((int?)ex.Error.StatusCode ?? 500, new { error = "Subscription service error" });
        }
        catch (System.Text.Json.JsonException ex)
        {
            return StatusCode(500, new { error = "Failed to process subscription response", details = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An unexpected error occurred", details = ex.Message });
        }
    }

    private async Task<Customer?> TryGetCustomer(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _maxioClient.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response?.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if ((int?)ex.Error.StatusCode == 404)
            {
                return null;
            }
            throw;
        }
    }

    private async Task<Customer?> CreateCustomer(string email, string reference, CancellationToken cancellationToken)
    {
        var createCustomer = new CreateCustomer
        {
            FirstName = "User",
            LastName = email.Split('@')[0],
            Email = email,
            Reference = reference
        };

        var createCustomerRequest = new CreateCustomerRequest
        {
            Customer = createCustomer
        };

        var response = await _maxioClient.Customers.CreateCustomer(createCustomerRequest, ct: cancellationToken);
        return response?.Customer;
    }

    private ActionResult<SubscriptionEnrollmentResponse> HandleSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var validationError))
        {
            return BadRequest(new { error = "Subscription validation error", details = validationError });
        }
        else if (ex.Error.TryGetRawError(out var rawError))
        {
            return StatusCode((int?)rawError.StatusCode ?? 400, new { error = "Failed to create subscription" });
        }

        return BadRequest(new { error = "Failed to create subscription" });
    }

    private ActionResult<SubscriptionEnrollmentResponse> HandleCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
        {
            return BadRequest(new { error = "Customer error", details = customerError });
        }
        else if (ex.Error.TryGetRawError(out var rawError))
        {
            return StatusCode((int?)rawError.StatusCode ?? 400, new { error = "Customer service error" });
        }

        return BadRequest(new { error = "Failed to process customer" });
    }
}

public class CreateSubscriptionDto
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionEnrollmentResponse
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
