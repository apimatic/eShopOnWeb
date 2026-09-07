using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _maxioClient;
    private readonly IHttpContextAccessor _contextAccessor;

    public ListMySubscriptionsEndpoint(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient maxioClient,
        IHttpContextAccessor contextAccessor)
    {
        _maxioClient = maxioClient;
        _contextAccessor = contextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
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
            var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(
                reference: userId,
                ct: CancellationToken.None);

            var customerId = customerResponse.Customer.Id;

            var subscriptions = new List<SubscriptionDto>();

            var subscriptionListResponse = await _maxioClient.Customers.ListCustomerSubscriptions(
                customerId: (int)customerId,
                ct: CancellationToken.None);

            foreach (var subResponse in subscriptionListResponse)
            {
                if (subResponse.Subscription != null)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = (int)subResponse.Subscription.Id,
                        State = subResponse.Subscription.State?.ToString() ?? "unknown",
                        ProductPriceInCents = (int)(subResponse.Subscription.ProductPriceInCents ?? 0),
                        CurrentPeriodStartedAt = subResponse.Subscription.CurrentPeriodStartedAt?.DateTime,
                        CurrentPeriodEndsAt = subResponse.Subscription.CurrentPeriodEndsAt?.DateTime,
                        NextAssessmentAt = subResponse.Subscription.NextAssessmentAt?.DateTime,
                        ProductName = subResponse.Subscription.Product?.Name ?? string.Empty,
                        ProductHandle = subResponse.Subscription.Product?.Handle ?? string.Empty
                    });
                }
            }

            return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = subscriptions });
        }
        catch (SdkException<RawError> ex) when ((int?)ex.Error.StatusCode == 404)
        {
            return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = new() });
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

    public class ListMySubscriptionsResponse
    {
        public List<SubscriptionDto> Subscriptions { get; set; } = new();
    }
}
