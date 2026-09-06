using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

public class ListUserSubscriptionsEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, ClaimsPrincipal user) =>
            {
                try
                {
                    var username = user.FindFirst(ClaimTypes.Name)?.Value;
                    if (string.IsNullOrEmpty(username))
                        return Results.Unauthorized();

                    var appUser = await userManager.FindByNameAsync(username);
                    if (appUser == null)
                        return Results.Unauthorized();

                    var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(appUser.Id);

                    var response = new ListUserSubscriptionsResponse
                    {
                        Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                        {
                            Id = s.Id,
                            State = s.State,
                            PriceInCents = s.ProductPriceInCents,
                            PriceInDollars = s.ProductPriceInCents / 100m,
                            ProductName = s.Product?.Name ?? "Unknown",
                            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                            CreatedAt = s.CreatedAt,
                            UpdatedAt = s.UpdatedAt
                        }).ToList()
                    };

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponse { Message = ex.Message });
                }
            })
            .Produces<ListUserSubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .WithName("ListUserSubscriptions");
    }
}

public class UserSubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = "";

    [JsonPropertyName("priceInCents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("priceInDollars")]
    public decimal PriceInDollars { get; set; }

    [JsonPropertyName("currentPeriodEndsAt")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";
}

public class ListUserSubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
