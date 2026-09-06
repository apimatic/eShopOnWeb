using System;
using System.Collections.Generic;
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
using MaxioAdvancedBilling.Models.Enums;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public class ListSubscriptionsEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, MaxioAdvancedBillingClient client, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(httpContext, client, userManager);
            })
           .Produces<ListSubscriptionsResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionsEndpoints")
           .WithName("ListSubscriptions");
    }

    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient client)
    {
        return Results.Ok(new ListSubscriptionsResponse());
    }

    private async Task<IResult> HandleAsync(HttpContext httpContext, MaxioAdvancedBillingClient client, UserManager<ApplicationUser> userManager)
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
            // Get customer by reference
            var customerResponse = await client.Customers.ReadCustomerByReference(reference: userId, ct: default);
            var customerId = customerResponse.Customer?.Id ?? 0;

            if (customerId == 0)
            {
                // User has no customer in Maxio yet
                return Results.Ok(new ListSubscriptionsResponse());
            }

            // Get subscriptions for this customer
            var subscriptions = await client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: default);

            var response = new ListSubscriptionsResponse();
            if (subscriptions != null)
            {
                foreach (var subResponse in subscriptions)
                {
                    if (subResponse?.Subscription != null)
                    {
                        var sub = new SubscriptionDto
                        {
                            Id = subResponse.Subscription.Id ?? 0,
                            CustomerId = subResponse.Subscription.Customer?.Id ?? 0,
                            ProductId = subResponse.Subscription.Product?.Id ?? 0,
                            ProductHandle = subResponse.Subscription.Product?.Handle ?? string.Empty,
                            State = subResponse.Subscription.State?.Value ?? string.Empty,
                            BalanceInCents = subResponse.Subscription.BalanceInCents ?? 0,
                            CurrentPeriodStartsAt = subResponse.Subscription.CurrentPeriodStartedAt,
                            CurrentPeriodEndsAt = subResponse.Subscription.CurrentPeriodEndsAt,
                            NextAssessmentAt = subResponse.Subscription.NextAssessmentAt,
                            ActivatedAt = subResponse.Subscription.ActivatedAt
                        };
                        response.Subscriptions.Add(sub);
                    }
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Customer not found
                return Results.Ok(new ListSubscriptionsResponse());
            }

            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}
