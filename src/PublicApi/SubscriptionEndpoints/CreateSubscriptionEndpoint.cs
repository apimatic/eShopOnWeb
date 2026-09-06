using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
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
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates a subscription for the logged-in user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, AdvancedBillingClient>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, AdvancedBillingClient client, HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsync(request, client, httpContext, ct);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, AdvancedBillingClient client, CancellationToken ct = default)
    {
        // This overload is for the interface; actual logic is in the 4-parameter version
        throw new NotImplementedException("Use the 4-parameter overload");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, AdvancedBillingClient client, HttpContext httpContext, CancellationToken ct)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            // Get user ID from JWT claims for idempotency
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Try to lookup existing customer by reference (user ID)
            int? customerId = null;
            try
            {
                var existingCustomerResponse = await client.Customers.ReadCustomerByReference(userId, ct);
                if (existingCustomerResponse.Customer != null)
                {
                    customerId = existingCustomerResponse.Customer.Id;
                }
            }
            catch (SdkException<RawError> ex)
            {
                // If customer not found (404), we'll create a new one
                // For other errors, let it bubble up
                if ((int)(ex.Error.StatusCode ?? System.Net.HttpStatusCode.InternalServerError) != 404)
                {
                    return Results.StatusCode((int)(ex.Error.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
                }
            }
            catch (JsonException)
            {
                return Results.StatusCode(500);
            }

            // Create customer if not found
            if (!customerId.HasValue)
            {
                try
                {
                    var createCustomerRequest = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.UserName?.Split('@')[0] ?? "User",
                            LastName = "Account",
                            Email = user.Email ?? string.Empty,
                            Reference = userId
                        }
                    };

                    var customerResponse = await client.Customers.CreateCustomer(createCustomerRequest, ct);
                    if (customerResponse.Customer == null)
                    {
                        return Results.BadRequest(new { error = "Failed to create customer" });
                    }
                    customerId = customerResponse.Customer.Id;
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    // Handle typed error responses
                    if (ex.Error.TryGetCustomerErrorResponse1(out var validationError))
                    {
                        return Results.BadRequest(new { error = "Validation error creating customer" });
                    }
                    else if (ex.Error.TryGetRawError(out var rawError))
                    {
                        return Results.StatusCode((int)(rawError.StatusCode ?? System.Net.HttpStatusCode.BadRequest));
                    }
                    return Results.BadRequest(new { error = "Failed to create customer" });
                }
                catch (JsonException)
                {
                    return Results.StatusCode(500);
                }
            }

            // Create subscription
            try
            {
                var productHandle = request.PlanHandle;

                var createSubscriptionRequest = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId,
                        Reference = userId
                    }
                };

                var subscriptionResponse = await client.Subscriptions.CreateSubscription(createSubscriptionRequest, ct);

                if (subscriptionResponse.Subscription == null)
                {
                    return Results.BadRequest(new { error = "Failed to create subscription" });
                }

                response.Subscription = new SubscriptionDto
                {
                    Id = subscriptionResponse.Subscription.Id ?? 0,
                    State = subscriptionResponse.Subscription.State?.Value ?? string.Empty,
                    ActivatedAt = subscriptionResponse.Subscription.ActivatedAt,
                    CurrentPeriodEndsAt = subscriptionResponse.Subscription.CurrentPeriodEndsAt,
                    CanceledAt = subscriptionResponse.Subscription.CanceledAt,
                    Reference = subscriptionResponse.Subscription.Reference,
                    Product = subscriptionResponse.Subscription.Product != null ? new SubscriptionPlanDto
                    {
                        Id = subscriptionResponse.Subscription.Product.Id ?? 0,
                        Handle = subscriptionResponse.Subscription.Product.Handle ?? string.Empty,
                        Name = subscriptionResponse.Subscription.Product.Name ?? string.Empty,
                        Description = subscriptionResponse.Subscription.Product.Description,
                        PriceInCents = subscriptionResponse.Subscription.Product.PriceInCents ?? 0,
                        Interval = subscriptionResponse.Subscription.Product.Interval,
                        IntervalUnit = subscriptionResponse.Subscription.Product.IntervalUnit?.Value
                    } : null
                };

                return Results.Created($"api/subscriptions/{subscriptionResponse.Subscription.Id}", response);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // Handle typed error responses
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    return Results.BadRequest(new { error = "Validation error creating subscription" });
                }
                else if (ex.Error.TryGetRawError(out var rawError))
                {
                    return Results.StatusCode((int)(rawError.StatusCode ?? System.Net.HttpStatusCode.BadRequest));
                }
                return Results.BadRequest(new { error = "Failed to create subscription" });
            }
            catch (JsonException)
            {
                return Results.StatusCode(500);
            }
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
