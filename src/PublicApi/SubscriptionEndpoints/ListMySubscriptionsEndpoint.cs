using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, MaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public ListMySubscriptionsEndpoint(
        UserManager<ApplicationUser> userManager,
        IRepository<Subscription> subscriptionRepository)
    {
        _userManager = userManager;
        _subscriptionRepository = subscriptionRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioSubscriptionService service, HttpContext context) =>
            {
                return await HandleAsyncWithContext(new ListMySubscriptionsRequest(), service, context);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    private async Task<IResult> HandleAsyncWithContext(ListMySubscriptionsRequest request, MaxioSubscriptionService service, HttpContext context)
    {
        try
        {
            var userName = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Results.BadRequest(new { error = "User not found" });
            }

            var customerId = await service.GetOrCreateCustomerAsync(
                userId: user.Id,
                email: user.Email ?? string.Empty,
                firstName: user.UserName?.Split('@')[0] ?? "User",
                lastName: string.Empty);

            var subscriptions = await service.ListUserSubscriptionsAsync(customerId);

            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductHandle = s.ProductHandle,
                    NextBillingAt = s.NextBillingAt,
                    PriceInDollars = s.PriceInCents / 100m
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, MaxioSubscriptionService service)
    {
        throw new NotImplementedException("Use HandleAsyncWithContext instead");
    }
}

public class ListMySubscriptionsRequest { }

public class ListMySubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public decimal PriceInDollars { get; set; }
}
