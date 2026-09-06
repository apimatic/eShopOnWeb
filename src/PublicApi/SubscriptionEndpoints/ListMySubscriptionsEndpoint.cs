using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get subscriptions for the authenticated user
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext context, MaxioAdvancedBillingClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<ListMySubscriptionsEndpoint> logger) =>
            {
                return await HandleAsync(context, maxioClient, userManager, logger);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithName("ListMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(
        HttpContext context,
        MaxioAdvancedBillingClient maxioClient,
        UserManager<ApplicationUser> userManager,
        ILogger<ListMySubscriptionsEndpoint> logger)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        try
        {
            // Extract username from JWT claims
            var username = context.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
            {
                response.Subscriptions = new();
                return Results.Ok(response);
            }

            // Get the user
            var user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                response.Subscriptions = new();
                return Results.Ok(response);
            }

            // Look up customer by reference
            IReadOnlyList<SubscriptionResponse>? subscriptions = null;
            try
            {
                var customerResponse = await maxioClient.Customers.ReadCustomerByReference(
                    reference: user.Id,
                    ct: default);

                if (customerResponse?.Customer?.Id != null)
                {
                    subscriptions = await maxioClient.Customers.ListCustomerSubscriptions(
                        customerId: customerResponse.Customer.Id.Value,
                        ct: default);
                }
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
            {
                // Customer not found, return empty list
                subscriptions = Array.Empty<SubscriptionResponse>();
            }

            if (subscriptions != null)
            {
                foreach (var subscriptionResponse in subscriptions)
                {
                    if (subscriptionResponse?.Subscription != null)
                    {
                        response.Subscriptions.Add(MapToDto(subscriptionResponse.Subscription));
                    }
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            logger.LogError($"Error listing subscriptions: HTTP {(int)ex.Error.StatusCode}");
            return Results.BadRequest(new { error = "Failed to list subscriptions" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error listing subscriptions");
            return Results.StatusCode(500);
        }
    }

    private SubscriptionDto MapToDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.ToString() ?? "unknown",
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod?.ToString() ?? "unknown"
        };
    }
}
