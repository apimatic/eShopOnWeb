using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService, IHttpContextAccessor httpContextAccessor) =>
            {
                return await Handle(maxioService, httpContextAccessor);
            })
           .Produces<ListUserSubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> Handle(IMaxioService maxioService, IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Results.Unauthorized();
        }

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var customerReference = $"eshop-{userId}";
            var subscriptions = await maxioService.GetCustomerSubscriptionsAsync(customerReference);

            var subscriptionDtos = subscriptions.Select(s => new UserSubscriptionDto
            {
                SubscriptionId = s.Id,
                CustomerId = s.CustomerId,
                PlanHandle = s.ProductHandle,
                State = s.State,
                PricePerMonth = s.PriceInCents / 100m,
                NextBillingAt = s.NextBillingAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
            }).ToList();

            return Results.Ok(new ListUserSubscriptionsResponse
            {
                Subscriptions = subscriptionDtos
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public decimal PricePerMonth { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
