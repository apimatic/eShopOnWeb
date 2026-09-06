using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
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
        Summary = "Create a subscription",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract user ID from JWT token
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal == null)
            {
                return Unauthorized("User not authenticated");
            }

            var userIdClaim = principal.FindFirst("sub") ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
            {
                return Unauthorized("User ID claim not found in token");
            }

            string userId = userIdClaim.Value;
            var correlationId = request.ToString();

            // Step 1: Look up customer by reference (userId)
            MaxioAdvancedBilling.Models.Customer customer = null;
            try
            {
                var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(
                    reference: userId,
                    ct: cancellationToken);

                customer = customerResponse?.Customer;
            }
            catch (SdkException<RawError> ex)
            {
                // Customer not found (404) - create a new one
                if (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    // Extract email and name from claims if available
                    var email = principal.FindFirst("email")?.Value ?? $"user-{userId}@eshop.local";
                    var name = principal.FindFirst("name")?.Value ?? "eShop User";
                    var nameParts = name.Split(' ', 2);
                    string firstName = nameParts[0];
                    string lastName = nameParts.Length > 1 ? nameParts[1] : "User";

                    var createCustomerRequest = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = userId
                        }
                    };

                    var createResponse = await _maxioClient.Customers.CreateCustomer(
                        body: createCustomerRequest,
                        ct: cancellationToken);

                    customer = createResponse?.Customer;
                }
                else
                {
                    throw;
                }
            }

            if (customer?.Id == null)
            {
                return BadRequest("Failed to create or retrieve customer");
            }

            // Step 2: Create subscription
            var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = customer.Id,
                    Reference = $"{userId}-{request.ProductHandle}-{DateTime.UtcNow.Ticks}"
                }
            };

            var subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: cancellationToken);

            var subscription = subscriptionResponse?.Subscription;
            if (subscription?.Id == null)
            {
                return BadRequest("Failed to create subscription");
            }

            var priceInCents = subscription.ProductPriceInCents ?? 0;
            return Ok(new CreateSubscriptionResponse(
                CorrelationId: correlationId,
                SubscriptionId: (int)(subscription.Id ?? 0),
                State: subscription.State?.Value ?? "unknown",
                ProductHandle: request.ProductHandle,
                CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
                NextAssessmentAt: subscription.NextAssessmentAt,
                ProductPricePerMonth: priceInCents / 100m,
                Reference: subscription.Reference
            ));
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // Handle typed subscription creation errors
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                return BadRequest($"Subscription creation error: {string.Join(", ", errorList.Errors ?? new System.Collections.Generic.List<string>())}");
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                var statusCode = (int)(rawError.StatusCode);
                return StatusCode(statusCode,
                    $"Subscription creation failed: {rawError.ReadAsString()}");
            }
            return BadRequest("Failed to create subscription");
        }
        catch (SdkException<RawError> ex)
        {
            var statusCode = (int)(ex.Error.StatusCode);
            return StatusCode(statusCode,
                $"Maxio error: {ex.Error.ReadAsString()}");
        }
        catch (Exception ex)
        {
            return BadRequest($"Error creating subscription: {ex.Message}");
        }
    }
}
