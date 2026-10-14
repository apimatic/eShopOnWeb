using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var plans = app.MapGet("api/subscription-plans", async (MaxioSubscriptionService service, CancellationToken ct) =>
        {
            var response = await service.GetPlansAsync(ct);
            return response.IsTruncated ? Results.Json(response, statusCode: StatusCodes.Status206PartialContent) : Results.Ok(response);
        });
        plans.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        plans.WithTags("Subscriptions").Produces<SubscriptionPlansResponse>();

        var subscribe = app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext context, MaxioSubscriptionService service, CancellationToken ct) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            try { return Results.Ok(await service.SubscribeAsync(userId, request.ProductHandle, ct)); }
            catch (SubscriptionValidationException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (SubscriptionProviderException ex) { return Results.Json(new { message = ex.Message }, statusCode: (int)ex.StatusCode); }
        });
        subscribe.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        subscribe.WithTags("Subscriptions").Produces<SubscriptionDto>();

        var mine = app.MapGet("api/my-subscriptions", async (HttpContext context, MaxioSubscriptionService service, CancellationToken ct) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrWhiteSpace(userId) ? Results.Unauthorized() : Results.Ok(await service.GetMySubscriptionsAsync(userId, ct));
        });
        mine.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        mine.WithTags("Subscriptions").Produces<MySubscriptionsResponse>();
    }
}
