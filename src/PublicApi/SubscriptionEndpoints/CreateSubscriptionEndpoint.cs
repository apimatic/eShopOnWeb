using System;
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

public partial class CreateSubscriptionEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app, UserManager<ApplicationUser> userManager)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateRequest request, ClaimsPrincipal user, IMaxioService maxioService) =>
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

                var subscription = await maxioService.CreateSubscriptionAsync(
                    request.ProductHandle,
                    customer.Id,
                    userReference
                );

                var response = new CreateResponse(Guid.NewGuid())
                {
                    Subscription = new SubscriptionDto
                    {
                        Id = subscription.Id,
                        CustomerId = subscription.CustomerId,
                        State = subscription.State,
                        ProductHandle = subscription.ProductHandle,
                        CreatedAt = subscription.CreatedAt,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                        NextBillingAt = subscription.NextAssessmentAt
                    }
                };

                return Results.Created($"/api/subscriptions/{subscription.Id}", response);
            })
            .Produces<CreateResponse>(201)
            .WithTags("SubscriptionEndpoints");
    }
}
