using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public partial class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(MaxioAdvancedBillingClient client, IConfiguration configuration, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionApiRequest request, HttpContext context, MaxioAdvancedBillingClient client, IConfiguration config, ILogger<CreateSubscriptionEndpoint> logger) =>
            {
                return await HandleAsync(request, context, client, config, logger);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        // This interface method is not used; the implementation is in the lambda above
        return Results.StatusCode(500);
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request, HttpContext context, MaxioAdvancedBillingClient client, IConfiguration config, ILogger<CreateSubscriptionEndpoint> logger)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        // Get the authenticated user
        var userNameClaim = context.User.FindFirst(ClaimTypes.Name);
        if (userNameClaim == null || string.IsNullOrEmpty(userNameClaim.Value))
        {
            logger.LogWarning("Subscription creation attempted without valid user claim");
            return Results.Unauthorized();
        }

        var userName = userNameClaim.Value;
        var maxioSettings = config.GetSection("Maxio").Get<MaxioSettings>();

        if (maxioSettings?.ProductFamilyHandle == null || string.IsNullOrEmpty(request.ProductHandle))
        {
            return Results.BadRequest("Product handle or Maxio configuration missing");
        }

        try
        {
            // Look up or create customer
            int customerId;
            try
            {
                var existingCustomer = await client.Customers.ReadCustomerByReference(
                    reference: userName,
                    ct: CancellationToken.None);
                customerId = existingCustomer?.Customer?.Id ?? 0;

                if (customerId == 0)
                {
                    logger.LogWarning("Customer lookup returned no ID for reference {Reference}", userName);
                    return Results.StatusCode(500);
                }
            }
            catch (SdkException<RawError> exNotFound) when (exNotFound.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Customer doesn't exist, create one
                var createCustomerRequest = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = "eShop",
                        LastName = "Customer",
                        Email = $"{userName}@eshop.local",
                        Reference = userName
                    }
                };

                try
                {
                    var newCustomer = await client.Customers.CreateCustomer(
                        body: createCustomerRequest,
                        ct: CancellationToken.None);
                    customerId = newCustomer?.Customer?.Id ?? 0;

                    if (customerId == 0)
                    {
                        logger.LogWarning("Customer creation returned no ID for reference {Reference}", userName);
                        return Results.StatusCode(500);
                    }

                    logger.LogInformation("Created new Maxio customer {CustomerId} for reference {Reference}", customerId, userName);
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    logger.LogError("Error creating Maxio customer for reference {Reference}", userName);
                    if (ex.Error.TryGetCustomerErrorResponse1(out var errorResp))
                    {
                        return Results.BadRequest($"Failed to create customer: {errorResp}");
                    }
                    return Results.StatusCode(422);
                }
            }

            // Create subscription
            var createSubRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = customerId
                }
            };

            var subscription = await client.Subscriptions.CreateSubscription(
                body: createSubRequest,
                ct: CancellationToken.None);

            if (subscription?.Subscription != null)
            {
                var sub = subscription.Subscription;
                response.Subscription = new SubscriptionDto
                {
                    Id = sub.Id ?? 0,
                    State = sub.State?.Value ?? "",
                    ProductName = sub.Product?.Name ?? "",
                    ProductHandle = sub.Product?.Handle ?? "",
                    CurrentBillingAmountInCents = sub.CurrentBillingAmountInCents ?? 0,
                    CurrentPeriodStartsAt = sub.CurrentPeriodStartedAt,
                    CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                    NextAssessmentAt = sub.NextAssessmentAt,
                    ActivatedAt = sub.ActivatedAt,
                    CanceledAt = sub.CanceledAt
                };

                logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", response.Subscription.Id, customerId);
                return Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
            }

            return Results.StatusCode(500);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            logger.LogError("Error creating subscription for product {ProductHandle}", request.ProductHandle);
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                return Results.BadRequest($"Subscription creation failed: {string.Join(", ", errors.Errors ?? new System.Collections.Generic.List<string>())}");
            }
            return Results.StatusCode(422);
        }
        catch (SdkException<RawError> ex)
        {
            logger.LogError(ex, "Unexpected Maxio error creating subscription");
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating subscription");
            return Results.StatusCode(500);
        }
    }
}
