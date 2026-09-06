using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists subscriptions for the logged-in user
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, AdvancedBillingClient>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (AdvancedBillingClient client, HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsync(new EmptyRequest(), client, httpContext, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, AdvancedBillingClient client, CancellationToken ct = default)
    {
        // This overload is for the interface; actual logic is in the 4-parameter version
        throw new NotImplementedException("Use the 4-parameter overload");
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, AdvancedBillingClient client, HttpContext httpContext, CancellationToken ct)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        try
        {
            // Get user ID from JWT claims
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

            // Try to lookup customer by reference
            try
            {
                var customerResponse = await client.Customers.ReadCustomerByReference(userId, ct);
                if (customerResponse.Customer == null)
                {
                    // Customer doesn't exist, return empty list
                    return Results.Ok(response);
                }

                var customerId = customerResponse.Customer.Id;
                if (!customerId.HasValue)
                {
                    return Results.Ok(response);
                }

                // List subscriptions for this customer
                var subscriptions = await client.Customers.ListCustomerSubscriptions(customerId.Value, ct);

                foreach (var subscriptionResponse in subscriptions)
                {
                    if (subscriptionResponse.Subscription != null)
                    {
                        response.Subscriptions.Add(new SubscriptionDto
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
                        });
                    }
                }

                return Results.Ok(response);
            }
            catch (SdkException<RawError> ex)
            {
                // If customer not found (404), return empty list
                if ((int)(ex.Error.StatusCode ?? System.Net.HttpStatusCode.InternalServerError) == 404)
                {
                    return Results.Ok(response);
                }
                return Results.StatusCode((int)(ex.Error.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
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

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
