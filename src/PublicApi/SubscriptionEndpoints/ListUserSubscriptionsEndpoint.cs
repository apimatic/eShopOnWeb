using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class ListUserSubscriptionsEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app, UserManager<ApplicationUser> userManager)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IMaxioService maxioService) =>
            {
                var userName = user.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                var appUser = await userManager.FindByNameAsync(userName);
                if (appUser == null)
                {
                    return Results.NotFound();
                }

                var userReference = appUser.Id;
                var customer = await maxioService.GetOrCreateCustomerAsync(
                    userReference,
                    appUser.UserName ?? "User",
                    appUser.UserName ?? "User",
                    appUser.Email ?? ""
                );

                var subscriptions = await maxioService.GetCustomerSubscriptionsAsync(customer.Id);
                var response = new ListResponse(Guid.NewGuid())
                {
                    Subscriptions = subscriptions
                        .Select(s => new UserSubscriptionDto
                        {
                            Id = s.Id,
                            State = s.State,
                            ProductHandle = s.ProductHandle,
                            ProductName = s.Product?.Name,
                            Price = s.Product != null ? s.Product.PriceInCents / 100m : null,
                            CreatedAt = s.CreatedAt,
                            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                            NextBillingAt = s.NextAssessmentAt
                        })
                        .ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ListResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
