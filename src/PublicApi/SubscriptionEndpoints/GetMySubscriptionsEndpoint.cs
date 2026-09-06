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

public class GetMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionService service) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscriptions = await service.GetUserSubscriptionsAsync(userId);
                    var response = new MySubscriptionsResponse
                    {
                        Subscriptions = subscriptions.Select(s => new UserSubscriptionResponse
                        {
                            SubscriptionId = s.SubscriptionId,
                            PlanHandle = s.PlanHandle,
                            Status = s.Status,
                            ActivatedAt = s.ActivatedAt,
                            NextBillingAt = s.NextBillingAt,
                            Price = s.Price
                        }).ToList()
                    };
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.StatusCode(500);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}

public class MySubscriptionsResponse
{
    public List<UserSubscriptionResponse> Subscriptions { get; set; } = new();
}

public class UserSubscriptionResponse
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal Price { get; set; }
}
