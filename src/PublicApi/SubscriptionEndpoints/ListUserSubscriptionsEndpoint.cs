using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List user's subscriptions
/// </summary>
public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, ListUserSubscriptionsRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioSubscriptionService service, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                return await HandleAsync(new ListUserSubscriptionsRequest { UserId = userId }, service);
            })
           .Produces<ListUserSubscriptionsResponse>()
           .Produces(401)
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListUserSubscriptionsRequest request, MaxioSubscriptionService service)
    {
        var subscriptions = await service.GetUserSubscriptionsAsync(request.UserId!);
        var dtos = subscriptions.ConvertAll(UserSubscriptionDto.FromService);
        return Results.Ok(new ListUserSubscriptionsResponse { Subscriptions = dtos });
    }
}

public class ListUserSubscriptionsRequest
{
    public string? UserId { get; set; }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public System.DateTime CreatedAt { get; set; }
    public System.DateTime? NextBillingAt { get; set; }
    public System.DateTime? CurrentPeriodEndsAt { get; set; }

    public static UserSubscriptionDto FromService(SubscriptionDto subscription)
    {
        return new UserSubscriptionDto
        {
            Id = subscription.Id,
            ProductName = subscription.ProductName,
            ProductHandle = subscription.ProductHandle,
            State = subscription.State,
            CreatedAt = subscription.CreatedAt,
            NextBillingAt = subscription.NextBillingAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }
}
