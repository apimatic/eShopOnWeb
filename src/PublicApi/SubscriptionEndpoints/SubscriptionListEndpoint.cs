using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionListEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;

    public SubscriptionListEndpoint(MaxioAdvancedBillingClient client)
    {
        _client = client;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleMySubscriptions(user, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    async Task<IResult> IEndpoint<IResult>.HandleAsync()
    {
        return Results.BadRequest();
    }

    public async Task<IResult> HandleMySubscriptions(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        try
        {
            var userName = user?.FindFirst(ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var customerReference = $"{userName}";

            var customer = await GetCustomerAsync(customerReference, cancellationToken);
            if (customer == null || customer.Id == null)
            {
                return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = [] });
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id.Value,
                ct: cancellationToken);

            var result = subscriptions
                .Select(s => new SubscriptionDto
                {
                    Id = s.Subscription?.Id,
                    State = s.Subscription?.State?.ToString(),
                    ProductPriceInCents = s.Subscription?.ProductPriceInCents,
                    CurrentPeriodEndsAt = s.Subscription?.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.Subscription?.NextAssessmentAt,
                    ActivatedAt = s.Subscription?.ActivatedAt,
                    CreatedAt = s.Subscription?.CreatedAt,
                    Product = s.Subscription?.Product != null ? new SubscriptionPlanDto
                    {
                        Id = s.Subscription.Product.Id,
                        Name = s.Subscription.Product.Name,
                        Handle = s.Subscription.Product.Handle,
                        PriceInCents = s.Subscription.Product.PriceInCents,
                        Interval = s.Subscription.Product.Interval,
                        IntervalUnit = s.Subscription.Product.IntervalUnit?.ToString()
                    } : null
                })
                .ToList();

            return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = result });
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception)
        {
            return Results.StatusCode(500);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> GetCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var customerResponse = await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: cancellationToken);
            return customerResponse.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}

public class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = [];
}
