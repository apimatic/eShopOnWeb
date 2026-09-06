using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, HttpContext context, MaxioAdvancedBillingClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger) =>
            {
                return await HandleAsync(request, context, maxioClient, userManager, logger);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(
        SubscribeRequest request,
        HttpContext context,
        MaxioAdvancedBillingClient maxioClient,
        UserManager<ApplicationUser> userManager,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            // Extract username from JWT claims
            var username = context.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
            {
                response.Success = false;
                response.Message = "User not authenticated";
                return Results.Unauthorized();
            }

            // Get the user
            var user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found";
                return Results.NotFound();
            }

            // Use user ID as the reference for idempotency
            var customerReference = user.Id;

            // Try to look up existing customer by reference (idempotent)
            int customerId;
            try
            {
                var existingCustomer = await maxioClient.Customers.ReadCustomerByReference(
                    reference: customerReference,
                    ct: default);

                if (existingCustomer?.Customer?.Id != null)
                {
                    customerId = existingCustomer.Customer.Id.Value;
                }
                else
                {
                    // Customer reference not found, create new customer
                    customerId = await CreateCustomer(user, customerReference, maxioClient);
                }
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
            {
                // Customer not found, create new one
                customerId = await CreateCustomer(user, customerReference, maxioClient);
            }

            // Create subscription
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = customerId,
                    Reference = customerReference
                }
            };

            var subscriptionResponse = await maxioClient.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: default);

            if (subscriptionResponse?.Subscription != null)
            {
                response.Subscription = MapToDto(subscriptionResponse.Subscription);
                response.Success = true;
                response.Message = $"Subscription created successfully. State: {response.Subscription.State}";
                return Results.Created($"/api/subscriptions/{subscriptionResponse.Subscription.Id}", response);
            }

            response.Success = false;
            response.Message = "Failed to create subscription";
            return Results.BadRequest(response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                logger.LogError($"Subscription creation failed with validation errors");
                response.Success = false;
                response.Message = "Subscription creation failed: validation error";
                return Results.BadRequest(response);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                logger.LogError($"Subscription creation failed: HTTP {(int)rawError.StatusCode}");
                response.Success = false;
                response.Message = "Subscription creation failed";
                return Results.StatusCode((int)rawError.StatusCode);
            }

            response.Success = false;
            response.Message = "Subscription creation failed";
            return Results.BadRequest(response);
        }
        catch (SdkException<RawError> ex)
        {
            logger.LogError($"Unexpected error creating subscription: HTTP {(int)ex.Error.StatusCode}");
            response.Success = false;
            response.Message = "Unexpected error";
            return Results.StatusCode((int)ex.Error.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating subscription");
            response.Success = false;
            response.Message = "Internal error";
            return Results.StatusCode(500);
        }
    }

    private async Task<int> CreateCustomer(ApplicationUser user, string reference, MaxioAdvancedBillingClient maxioClient)
    {
        var createCustomerRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "Customer",
                LastName = user.Id,
                Email = user.Email ?? string.Empty,
                Reference = reference
            }
        };

        var customerResponse = await maxioClient.Customers.CreateCustomer(
            body: createCustomerRequest,
            ct: default);

        return customerResponse?.Customer?.Id ?? throw new InvalidOperationException("Failed to create customer");
    }

    private SubscriptionDto MapToDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.ToString() ?? "unknown",
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod?.ToString() ?? "unknown"
        };
    }
}
