using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IHttpContextAccessor>
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IHttpContextAccessor contextAccessor) =>
            {
                return await HandleAsync(contextAccessor);
            })
           .Produces<ListMySubscriptionsResponse>()
           .RequireAuthorization()
           .WithName("ListMySubscriptions")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IHttpContextAccessor contextAccessor)
    {
        try
        {
            var httpContext = contextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Results.BadRequest("No HTTP context");
            }

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound("User not found");
            }

            if (!user.MaxioCustomerId.HasValue)
            {
                return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = [] });
            }

            var maxioSubscriptions = await _maxioClient.ListSubscriptionsByCustomerIdAsync(user.MaxioCustomerId.Value);

            var subscriptions = maxioSubscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Subscription.Id,
                State = s.Subscription.State,
                ProductName = s.Subscription.Product.Name,
                MonthlyPrice = s.Subscription.Product_price_in_cents.HasValue
                    ? s.Subscription.Product_price_in_cents.Value / 100m
                    : null,
                NextBillingDate = s.Subscription.Current_period_ends_at,
                ActivatedAt = s.Subscription.Activated_at ?? DateTime.MinValue,
                CreatedAt = s.Subscription.Created_at
            }).ToList();

            return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = subscriptions });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
