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

public class SubscribeRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscribeResponse : Microsoft.eShopWeb.PublicApi.BaseResponse
{
    public SubscribeResponse() { }
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public int SubscriptionId { get; set; }
    public bool Created { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string NextBillingDate { get; set; } = string.Empty;
}

public class SubscribeEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest req, IMaxioBillingService service, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
            {
                if (user.Identity == null || !user.Identity.IsAuthenticated)
                    return Results.Unauthorized();

                var name = user.Identity.Name ?? "";
                var appUser = await userManager.FindByNameAsync(name);
                if (appUser == null)
                    return Results.Unauthorized();

                var userRef = appUser.Id;
                var first = appUser.UserName?.Split('@').FirstOrDefault() ?? "User";
                var result = await service.SubscribeAsync(userRef, first, "", appUser.Email ?? "", req.ProductHandle);
                var response = new SubscribeResponse(Guid.NewGuid())
                {
                    SubscriptionId = result.SubscriptionId,
                    Created = result.Created,
                    State = result.State,
                    ProductName = result.ProductName,
                    Price = result.Price,
                    NextBillingDate = result.NextBillingDate
                };
                return Results.Ok(response);
            })
            .Produces<SubscribeResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }
}
