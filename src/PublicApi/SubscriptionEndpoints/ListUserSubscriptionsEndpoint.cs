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
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, IMaxioService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService maxioService, IHttpContextAccessor httpContextAccessor) =>
            {
                return await HandleAsync(maxioService, httpContextAccessor);
            })
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints")
            .Produces<ListUserSubscriptionsResponse>()
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService, IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await maxioService.GetUserSubscriptionsAsync(userId);

            var dtos = subscriptions.Select(s => new UserSubscriptionDto
            {
                SubscriptionId = s.Id,
                State = s.State,
                ProductName = s.ProductName,
                ProductHandle = s.ProductHandle,
                Price = s.Price,
                Balance = s.Balance,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextBillingAt = s.NextBillingAt,
                CreatedAt = s.CreatedAt
            }).ToList();

            return Results.Ok(new ListUserSubscriptionsResponse { Subscriptions = dtos });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ListUserSubscriptionsResponse { Message = $"Error: {ex.Message}" });
        }
    }
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Balance { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    public string? Message { get; set; }
}
