using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionEndpoint.Request, CreateSubscriptionEndpoint.Dependencies>
{
    public sealed class Request
    {
        public string? ProductHandle { get; set; }
    }

    public sealed class Dependencies
    {
        public Dependencies(MaxioAdvancedBillingClient maxioClient, HttpContext httpContext, UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger)
        {
            MaxioClient = maxioClient;
            HttpContext = httpContext;
            UserManager = userManager;
            Logger = logger;
        }

        public MaxioAdvancedBillingClient MaxioClient { get; }
        public HttpContext HttpContext { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public ILogger<CreateSubscriptionEndpoint> Logger { get; }
    }

    public sealed class CreateSubscriptionResponse
    {
        public int SubscriptionId { get; set; }
        public string? State { get; set; }
        public string? ProductHandle { get; set; }
        public string? ProductName { get; set; }
        public long? ProductPriceInCents { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (Request request, Dependencies deps) =>
            {
                return await HandleAsync(request, deps);
            })
           .RequireAuthorization()
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status500InternalServerError)
           .WithTags("SubscriptionEndpoints")
           .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(Request request, Dependencies deps)
    {
        try
        {
            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest("ProductHandle is required");
            }

            var userIdClaim = deps.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim?.Value == null)
            {
                return Results.Unauthorized();
            }

            var userId = userIdClaim.Value;
            var user = await deps.UserManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            deps.Logger.LogInformation("Processing subscription for user {UserId} to plan {ProductHandle}", userId, request.ProductHandle);

            var customer = await EnsureCustomerExists(deps, userId, user);
            if (customer == null)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            var subscription = await CreateSubscription(deps, customer.Id ?? 0, request.ProductHandle);
            if (subscription == null)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id ?? 0,
                State = subscription.State?.ToString(),
                ProductHandle = subscription.Product?.Handle,
                ProductName = subscription.Product?.Name,
                ProductPriceInCents = subscription.Product?.PriceInCents,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt
            };

            deps.Logger.LogInformation("Successfully created subscription {SubscriptionId} for user {UserId}", subscription.Id, userId);
            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (SdkException<RawError> ex)
        {
            deps.Logger.LogError(ex, "Maxio API error: {Status}", ex.Error.StatusCode);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            deps.Logger.LogError(ex, "Error creating subscription");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<Customer?> EnsureCustomerExists(Dependencies deps, string userId, ApplicationUser user)
    {
        try
        {
            deps.Logger.LogInformation("Checking if customer exists for user {UserId}", userId);

            var existingCustomer = await deps.MaxioClient.Customers.ReadCustomerByReference(reference: userId, ct: default);
            if (existingCustomer?.Customer != null)
            {
                deps.Logger.LogInformation("Found existing customer {CustomerId} for user {UserId}", existingCustomer.Customer.Id, userId);
                return existingCustomer.Customer;
            }
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            deps.Logger.LogInformation("Customer not found for user {UserId}, creating new one", userId);
        }
        catch (Exception ex)
        {
            deps.Logger.LogError(ex, "Error checking for existing customer");
            return null;
        }

        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = user.UserName ?? "User",
                    LastName = userId,
                    Email = user.Email ?? $"{userId}@eshop.local",
                    Reference = userId
                }
            };

            var response = await deps.MaxioClient.Customers.CreateCustomer(body: createRequest, ct: default);
            deps.Logger.LogInformation("Created new customer {CustomerId} for user {UserId}", response?.Customer?.Id, userId);
            return response?.Customer;
        }
        catch (Exception ex)
        {
            deps.Logger.LogError(ex, "Error creating customer");
            return null;
        }
    }

    private async Task<Subscription?> CreateSubscription(Dependencies deps, int customerId, string productHandle)
    {
        try
        {
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await deps.MaxioClient.Subscriptions.CreateSubscription(body: subscriptionRequest, ct: default);
            return response?.Subscription;
        }
        catch (Exception ex)
        {
            deps.Logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            return null;
        }
    }
}
