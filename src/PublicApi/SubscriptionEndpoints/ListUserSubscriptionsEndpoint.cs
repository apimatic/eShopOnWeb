using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ListUserSubscriptionsEndpoint
{
    public static void MapListUserSubscriptions(this WebApplication app)
    {
        app.MapGet("api/my-subscriptions", ListUserSubscriptions)
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListUserSubscriptions");
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    private static async Task<IResult> ListUserSubscriptions(
        IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        try
        {
            var userId = user.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var appUser = await userManager.FindByNameAsync(userId);
            if (appUser == null)
            {
                return Results.Unauthorized();
            }

            var (customerId, _) = await subscriptionService.GetOrCreateCustomerAsync(
                appUser.Id, appUser.Email!, httpContext.RequestAborted);

            var subscriptions = await subscriptionService.ListUserSubscriptionsAsync(
                customerId, httpContext.RequestAborted);

            var response = new ListUserSubscriptionsResponse
            {
                Subscriptions = subscriptions.ConvertAll(s => new UserSubscriptionResponse
                {
                    Id = s.Id,
                    State = s.State,
                    ProductId = s.ProductId,
                    ProductHandle = s.ProductHandle,
                    Reference = s.Reference,
                    NextAssessmentAt = s.NextAssessmentAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    CreatedAt = s.CreatedAt
                })
            };

            return Results.Ok(response);
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionResponse> Subscriptions { get; set; } = new();
}

public class UserSubscriptionResponse
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public int? ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
