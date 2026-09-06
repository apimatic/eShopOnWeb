using System;
using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService, UserManager<ApplicationUser> userManager, HttpContext context) =>
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    return Results.NotFound("User not found");
                }

                // Get or create Maxio customer
                var customer = await maxioService.GetOrCreateCustomerAsync(
                    user.Email ?? "",
                    user.UserName ?? "",
                    user.UserName ?? "",
                    userId
                );

                if (customer == null)
                {
                    return Results.BadRequest("Failed to retrieve Maxio customer");
                }

                // Get subscriptions
                var subscriptions = await maxioService.GetCustomerSubscriptionsAsync(customer.Id);

                var response = new MySubscriptionsResponse
                {
                    Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                    {
                        Id = s.Id,
                        State = s.State,
                        ProductName = s.ProductName,
                        ProductHandle = s.ProductHandle,
                        Price = (decimal)s.PriceInCents / 100,
                        NextBillingDate = s.NextAssessmentAt,
                    }).ToList()
                };

                return Results.Ok(response);
            })
           .Produces<MySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class MySubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
