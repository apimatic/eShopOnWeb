using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List My Subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new UnauthorizedAccessException("No HTTP context available");

        var userId = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User identity not found in token");

        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(userId);

        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductHandle = s.ProductHandle,
            ProductName = s.ProductName,
            AmountInCents = s.AmountInCents,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingAt = s.NextBillingAt,
            ProductId = s.ProductId
        }).ToList();

        return Results.Ok(response);
    }
}
