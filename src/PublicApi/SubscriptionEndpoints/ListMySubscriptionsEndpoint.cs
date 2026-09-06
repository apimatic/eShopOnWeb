using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService maxioService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                var endpoint = new ListMySubscriptionsEndpoint();
                return await endpoint.HandleAsyncImpl(maxioService, userManager, httpContext);
            })
            .RequireAuthorization()
            .WithName("ListMySubscriptions")
            .Produces<ListMySubscriptionsResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<IResult> HandleAsyncImpl(IMaxioService maxioService, UserManager<ApplicationUser> userManager, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Get or create customer (if they don't have one yet, create one)
            var maxioCustomer = await maxioService.GetOrCreateCustomerAsync(
                userId,
                user.UserName?.Split("@")[0] ?? "User",
                "Customer",
                user.Email ?? "",
                default
            );

            // Get customer subscriptions
            var maxioSubscriptions = await maxioService.GetCustomerSubscriptionsAsync(maxioCustomer.Id, default);

            var response = new ListMySubscriptionsResponse
            {
                CustomerId = maxioCustomer.Id,
                Email = user.Email
            };

            response.Subscriptions.AddRange(maxioSubscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductHandle = s.ProductHandle,
                NextBillingAt = s.NextBillingAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                IsActive = s.State == "active"
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public bool IsActive { get; set; }
}

public class ListMySubscriptionsResponse
{
    public int CustomerId { get; set; }
    public string? Email { get; set; }
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
