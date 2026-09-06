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

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscription(this WebApplication app)
    {
        app.MapPost("api/subscriptions", CreateSubscription)
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
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

            if (!request.ProductId.HasValue)
            {
                return Results.BadRequest(new { error = "ProductId is required" });
            }

            var (customerId, _) = await subscriptionService.GetOrCreateCustomerAsync(
                appUser.Id, appUser.Email!, httpContext.RequestAborted);

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                customerId, request.ProductId.Value, appUser.Id, httpContext.RequestAborted);

            var response = new CreateSubscriptionResponse
            {
                Subscription = new SubscriptionResponse
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    CustomerId = subscription.CustomerId,
                    ProductId = subscription.ProductId,
                    Reference = subscription.Reference,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    CreatedAt = subscription.CreatedAt
                }
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public SubscriptionResponse? Subscription { get; set; }
}

public class SubscriptionResponse
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public int? CustomerId { get; set; }
    public int? ProductId { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
