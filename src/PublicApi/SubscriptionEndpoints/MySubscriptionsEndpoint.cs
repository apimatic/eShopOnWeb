using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get my subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioApiClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioApiClient maxio) =>
            {
                return await HandleAsync(maxio);
            })
           .Produces<ListMySubscriptionsResponse>()
           .Produces(400)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioApiClient maxio)
    {
        var response = new ListMySubscriptionsResponse();

        var user = _httpContextAccessor.HttpContext?.User;
        var email = user?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(email))
        {
            return Results.BadRequest(new { error = "Unable to determine user identity from token." });
        }

        var customer = await maxio.FindCustomerByEmailAsync(email);
        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxio.ListSubscriptionsByCustomerIdAsync(customer.Id);

        response.Subscriptions = new List<SubscriptionDto>();
        foreach (var sub in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDto
            {
                Id = sub.Id,
                State = sub.State,
                BalanceInCents = sub.BalanceInCents,
                TotalRevenueInCents = sub.TotalRevenueInCents,
                ProductPriceInCents = sub.ProductPriceInCents,
                CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                NextBillingDate = sub.NextAssessmentAt,
                ActivatedAt = sub.ActivatedAt,
                CreatedAt = sub.CreatedAt,
                ProductHandle = sub.Product?.Handle,
                ProductName = sub.Product?.Name,
                PlanInterval = sub.Product?.IntervalUnit,
                PlanIntervalCount = sub.Product?.Interval
            });
        }

        return Results.Ok(response);
    }
}
