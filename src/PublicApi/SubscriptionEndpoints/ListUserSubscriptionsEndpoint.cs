using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            ListUserSubscriptions)
           .Produces<ListUserSubscriptionsResponse>()
           .WithName("ListUserSubscriptions")
           .WithTags("Subscriptions")
           .RequireAuthorization();
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    private static async Task<IResult> ListUserSubscriptions(ClaimsPrincipal user, IReadRepository<UserSubscription> userSubscriptionRepository)
    {
        var response = new ListUserSubscriptionsResponse();

        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await userSubscriptionRepository.ListAsync(new UserSubscriptionsByUserIdSpec(userId));

            foreach (var sub in subscriptions)
            {
                response.Subscriptions.Add(new UserSubscriptionDto
                {
                    Id = sub.MaxioSubscriptionId,
                    ProductHandle = sub.ProductHandle,
                    State = sub.State,
                    NextBillingDate = sub.NextBillingAt,
                    CreatedAt = sub.CreatedAt
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Error = $"Error loading subscriptions: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    public string? Error { get; set; }
}

