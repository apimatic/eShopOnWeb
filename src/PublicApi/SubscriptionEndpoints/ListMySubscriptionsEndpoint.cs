using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListMySubscriptionsEndpoint
{
    public static void MapListMySubscriptions(this WebApplication app)
    {
        app.MapGet("api/my-subscriptions", ListMySubscriptions)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListMySubscriptions");
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    private static async Task<IResult> ListMySubscriptions(
        IMaxioSubscriptionService subscriptionService,
        IReadRepository<MaxioCustomer> customerRepository,
        HttpContext httpContext)
    {
        var response = new ListMySubscriptionsResponse();

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var customerSpec = new MaxioCustomerByUserIdSpec(userId);
            var maxioCustomer = await customerRepository.FirstOrDefaultAsync(customerSpec);

            if (maxioCustomer == null)
            {
                return Results.Ok(response);
            }

            var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(maxioCustomer.MaxioCustomerId);
            var activeSubscriptions = subscriptions
                .Where(s => s.State == "Active" || s.State == "active")
                .ToList();

            response.Subscriptions.AddRange(activeSubscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                MaxioCustomerId = s.CustomerId,
                MaxioSubscriptionId = s.Id,
                ProductHandle = string.Empty,
                PriceInCents = (decimal)s.PriceInCents,
                NextBillingAt = s.NextBillingAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}
