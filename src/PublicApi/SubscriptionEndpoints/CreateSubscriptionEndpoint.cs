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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioService maxioService,
                   UserManager<ApplicationUser> userManager, HttpContext context) =>
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
                    return Results.BadRequest("Failed to create/retrieve Maxio customer");
                }

                // Create subscription
                var subscription = await maxioService.CreateSubscriptionAsync(
                    customer.Id,
                    request.ProductHandle
                );

                return Results.Ok(new SubscriptionResponse
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    ProductName = subscription.ProductName,
                    ProductHandle = subscription.ProductHandle,
                    Price = (decimal)subscription.PriceInCents / 100,
                    NextBillingDate = subscription.NextAssessmentAt,
                });
            })
           .Produces<SubscriptionResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
