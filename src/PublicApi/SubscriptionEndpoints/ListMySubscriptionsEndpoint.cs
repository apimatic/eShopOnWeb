using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the caller's live subscription enrollments (from Maxio).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                var user = _httpContextAccessor.HttpContext?.User
                    ?? throw new UnauthorizedAccessException("No authenticated user identity was found on the request.");

                return await HandleCoreAsync(user, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        return HandleCoreAsync(_httpContextAccessor.HttpContext?.User!, subscriptionService);
    }

    private async Task<IResult> HandleCoreAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(user);

        var response = new ListMySubscriptionsResponse();
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State ?? string.Empty,
                PlanHandle = subscription.Product?.Handle ?? string.Empty,
                PlanName = subscription.Product?.Name ?? string.Empty,
                PriceInCents = subscription.ProductPriceInCents,
                Price = ListSubscriptionPlansEndpoint.FormatPrice(subscription.ProductPriceInCents),
                IntervalUnit = subscription.Product?.IntervalUnit ?? "month",
                NextBillingDate = subscription.CurrentPeriodEndsAt,
                MaxioCustomerId = subscription.Customer?.Id ?? 0
            });
        }

        return Results.Ok(response);
    }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
