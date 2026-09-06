using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly MaxioCustomerService _customerService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        MaxioCustomerService customerService,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _customerService = customerService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var ct = CancellationToken.None;

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { error = "User not authenticated" });
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "PlanHandle is required" });
            }

            var maxioCustomerId = await GetOrCreateMaxioCustomerAsync(userId, ct);

            var subscriptionBody = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = maxioCustomerId,
                    ProductHandle = request.PlanHandle
                }
            };

            try
            {
                var subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(subscriptionBody, ct);
                var subscription = subscriptionResponse.Subscription;

                if (subscription != null)
                {
                    response.Subscription = new SubscriptionDto
                    {
                        Id = subscription.Id,
                        State = subscription.State?.Value,
                        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                        NextAssessmentAt = subscription.NextAssessmentAt,
                        ActivatedAt = subscription.ActivatedAt,
                        ProductPriceInCents = subscription.ProductPriceInCents,
                        Product = subscription.Product != null ? new SubscriptionPlanDto
                        {
                            Handle = subscription.Product.Handle,
                            Name = subscription.Product.Name,
                            PriceInCents = subscription.Product.PriceInCents,
                            Interval = subscription.Product.Interval,
                            IntervalUnit = subscription.Product.IntervalUnit?.ToString()
                        } : null
                    };
                }

                return Results.Created($"/api/subscriptions/{subscription?.Id}", response);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var errorMessages = errorList.Errors?.ToList() ?? [];
                    return Results.BadRequest(new { errors = errorMessages });
                }
                throw;
            }
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<int> GetOrCreateMaxioCustomerAsync(string userId, CancellationToken ct)
    {
        var existingCustomerId = await _customerService.GetMaxioCustomerIdAsync(userId);
        if (existingCustomerId.HasValue)
        {
            return existingCustomerId.Value;
        }

        try
        {
            var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(userId, ct);
            var customerId = customerResponse.Customer?.Id;
            if (customerId.HasValue)
            {
                await _customerService.SaveMaxioCustomerMappingAsync(userId, customerId.Value);
                return customerId.Value;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Customer doesn't exist, create one
        }

        var createCustomerBody = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "Customer",
                LastName = userId,
                Email = $"{userId}@eshop.local",
                Reference = userId
            }
        };

        var createResponse = await _maxioClient.Customers.CreateCustomer(createCustomerBody, ct);
        var newCustomerId = createResponse.Customer?.Id;

        if (!newCustomerId.HasValue)
        {
            throw new InvalidOperationException("Failed to create Maxio customer");
        }

        await _customerService.SaveMaxioCustomerMappingAsync(userId, newCustomerId.Value);
        return newCustomerId.Value;
    }
}
