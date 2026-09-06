using System;
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

public class CreateSubscriptionEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, ClaimsPrincipal user) =>
            {
                try
                {
                    var username = user.FindFirst(ClaimTypes.Name)?.Value;
                    if (string.IsNullOrEmpty(username))
                        return Results.Unauthorized();

                    var appUser = await userManager.FindByNameAsync(username);
                    if (appUser == null)
                        return Results.Unauthorized();

                    if (string.IsNullOrWhiteSpace(request.ProductHandle))
                        return Results.BadRequest(new ErrorResponse { Message = "ProductHandle is required" });

                    var nameParts = (appUser.UserName ?? "User").Split(' ', 2);
                    var firstName = nameParts.Length > 0 ? nameParts[0] : "User";
                    var lastName = nameParts.Length > 1 ? nameParts[1] : "";

                    var subscription = await subscriptionService.CreateSubscriptionAsync(
                        appUser.Id,
                        appUser.Email ?? "",
                        firstName,
                        lastName,
                        request.ProductHandle);

                    var response = new CreateSubscriptionResponse
                    {
                        SubscriptionId = subscription.Id,
                        State = subscription.State,
                        PriceInCents = subscription.ProductPriceInCents,
                        PriceInDollars = subscription.ProductPriceInCents / 100m,
                        NextBillingDate = subscription.CurrentPeriodEndsAt,
                        CreatedAt = subscription.CreatedAt
                    };

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponse { Message = ex.Message });
                }
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("productHandle")]
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse
{
    [JsonPropertyName("subscriptionId")]
    public int SubscriptionId { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("priceInCents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("priceInDollars")]
    public decimal PriceInDollars { get; set; }

    [JsonPropertyName("nextBillingDate")]
    public string? NextBillingDate { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";
}

public class ErrorResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
