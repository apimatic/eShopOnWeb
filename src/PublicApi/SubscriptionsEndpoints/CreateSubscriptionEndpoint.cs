using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models.Enums;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionApiRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionApiRequest request, HttpContext httpContext, MaxioAdvancedBillingClient client, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, httpContext, client, userManager);
            })
           .Produces<CreateSubscriptionResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionsEndpoints")
           .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request)
    {
        // This method is required by the interface but we handle in AddRoute
        return Results.BadRequest();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request, HttpContext httpContext, MaxioAdvancedBillingClient client, UserManager<ApplicationUser> userManager)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            // Step 1: Get or create customer
            int customerId;
            try
            {
                var existingCustomer = await client.Customers.ReadCustomerByReference(reference: userId, ct: default);
                customerId = existingCustomer.Customer?.Id ?? 0;
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Create new customer
                    var createCustomerRequest = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.UserName ?? "User",
                            LastName = string.Empty,
                            Email = user.Email ?? string.Empty,
                            Reference = userId
                        }
                    };

                    var newCustomerResponse = await client.Customers.CreateCustomer(body: createCustomerRequest, ct: default);
                    customerId = newCustomerResponse.Customer?.Id ?? 0;

                    if (customerId == 0)
                    {
                        return Results.StatusCode(500);
                    }
                }
                else
                {
                    return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
                }
            }

            // Step 2: Check for existing subscription for this plan
            var existingSubscriptions = await client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: default);

            var existingSubscription = existingSubscriptions?
                .FirstOrDefault(s => s.Subscription?.Product?.Handle == request.ProductHandle &&
                                     (s.Subscription?.State == SubscriptionState.Active ||
                                      s.Subscription?.State == SubscriptionState.Pending));

            if (existingSubscription != null)
            {
                // Already subscribed to this plan
                return Results.Ok(new CreateSubscriptionResponse
                {
                    Subscription = MapToSubscriptionDto(existingSubscription.Subscription),
                    IsNewSubscription = false
                });
            }

            // Step 3: Create subscription
            var createSubRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            };

            var subscriptionResponse = await client.Subscriptions.CreateSubscription(body: createSubRequest, ct: default);

            if (subscriptionResponse?.Subscription == null)
            {
                return Results.StatusCode(500);
            }

            return Results.Ok(new CreateSubscriptionResponse
            {
                Subscription = MapToSubscriptionDto(subscriptionResponse.Subscription),
                IsNewSubscription = true
            });
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var err422))
            {
                return Results.BadRequest(new { error = "Invalid customer data" });
            }
            if (ex.Error.TryGetRawError(out var rawErr))
            {
                return Results.StatusCode((int?)rawErr.StatusCode ?? 500);
            }
            return Results.StatusCode(500);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var err422))
            {
                return Results.BadRequest(new { error = "Invalid subscription data" });
            }
            if (ex.Error.TryGetRawError(out var rawErr))
            {
                return Results.StatusCode((int?)rawErr.StatusCode ?? 500);
            }
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }

    private static SubscriptionDto MapToSubscriptionDto(Subscription? subscription)
    {
        if (subscription == null)
        {
            return new SubscriptionDto();
        }

        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            CustomerId = subscription.Customer?.Id ?? 0,
            ProductId = subscription.Product?.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            State = subscription.State?.Value ?? string.Empty,
            BalanceInCents = subscription.BalanceInCents ?? 0,
            CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt
        };
    }
}
