using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly MaxioCustomerService _customerService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(
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
        app.MapGet("api/my-subscriptions", HandleAsync)
            .RequireAuthorization()
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        var ct = CancellationToken.None;

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { error = "User not authenticated" });
            }

            var maxioCustomerId = await _customerService.GetMaxioCustomerIdAsync(userId);

            if (!maxioCustomerId.HasValue)
            {
                return Results.Ok(response);
            }

            try
            {
                var subscriptions = await _maxioClient.Customers.ListCustomerSubscriptions(maxioCustomerId.Value, ct);

                foreach (var subscriptionResponse in subscriptions)
                {
                    var subscription = subscriptionResponse.Subscription;
                    if (subscription != null)
                    {
                        response.Subscriptions.Add(new SubscriptionDto
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
                                IntervalUnit = subscription.Product.IntervalUnit?.Value
                            } : null
                        });
                    }
                }

                return Results.Ok(response);
            }
            catch (SdkException<RawError> ex)
            {
                return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
            }
        }
        catch (JsonException)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
