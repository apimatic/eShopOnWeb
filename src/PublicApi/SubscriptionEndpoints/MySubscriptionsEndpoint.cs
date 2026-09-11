using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CurrentPeriodEndsAt { get; set; } = string.Empty;
}

public class MySubscriptionsResponse : Microsoft.eShopWeb.PublicApi.BaseResponse
{
    public MySubscriptionsResponse() { }
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class MySubscriptionsEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioBillingService service, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
            {
                if (user.Identity == null || !user.Identity.IsAuthenticated)
                    return Results.Unauthorized();

                var name = user.Identity.Name ?? "";
                var appUser = await userManager.FindByNameAsync(name);
                if (appUser == null)
                    return Results.Unauthorized();

                var subs = await service.GetMySubscriptionsAsync(appUser.Id);
                return Results.Ok(new MySubscriptionsResponse(Guid.NewGuid())
                {
                    Subscriptions = subs.Select(s => new MySubscriptionDto
                    {
                        Id = s.Id,
                        ProductName = s.ProductName,
                        ProductHandle = s.ProductHandle,
                        State = s.State,
                        Price = s.Price,
                        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
                    }).ToList()
                });
            })
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }
}
