using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IHttpContextAccessor _contextAccessor;

    public CreateSubscriptionEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        IHttpContextAccessor contextAccessor)
    {
        _maxioClient = maxioClient;
        _contextAccessor = contextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var httpContext = _contextAccessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext not available");
        var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            int customerId;
            try
            {
                var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(
                    reference: userId,
                    ct: CancellationToken.None);
                customerId = (int)customerResponse.Customer.Id;
            }
            catch (SdkException<RawError> ex) when ((int?)ex.Error.StatusCode == 404)
            {
                var createCustomerBody = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = "Customer",
                        LastName = userId,
                        Email = $"{userId}@eshop.local",
                        Reference = userId,
                        Organization = "eShopOnWeb"
                    }
                };

                var createResponse = await _maxioClient.Customers.CreateCustomer(
                    body: createCustomerBody,
                    ct: CancellationToken.None);
                customerId = (int)createResponse.Customer.Id;
            }

            var createSubBody = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = request.ProductHandle,
                    ProductId = request.ProductId
                }
            };

            var subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(
                body: createSubBody,
                ct: CancellationToken.None);

            var subscription = subscriptionResponse.Subscription;
            if (subscription == null)
            {
                return Results.BadRequest("Failed to create subscription");
            }

            return Results.Created($"api/subscriptions/{subscription.Id}",
                new CreateSubscriptionResponse
                {
                    Subscription = new SubscriptionDto
                    {
                        Id = (int)subscription.Id,
                        State = subscription.State?.ToString() ?? "unknown",
                        ProductPriceInCents = (int)(subscription.ProductPriceInCents ?? 0),
                        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt?.DateTime,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt?.DateTime,
                        NextAssessmentAt = subscription.NextAssessmentAt?.DateTime,
                        ProductName = subscription.Product?.Name ?? string.Empty,
                        ProductHandle = subscription.Product?.Handle ?? string.Empty
                    }
                });
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationErrors))
            {
                return Results.BadRequest(new { errors = validationErrors?.Errors });
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                return Results.StatusCode((int?)raw.StatusCode ?? 500);
            }

            return Results.BadRequest();
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var custErrors))
            {
                return Results.BadRequest(new { errors = custErrors?.Errors });
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                return Results.StatusCode((int?)raw.StatusCode ?? 500);
            }

            return Results.BadRequest();
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (JsonException)
        {
            return Results.StatusCode(500);
        }
    }

    public class CreateSubscriptionResponse
    {
        public SubscriptionDto Subscription { get; set; } = new();
    }
}
