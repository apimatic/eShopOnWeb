using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEndpoints : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService service, CancellationToken ct) => Results.Ok(await service.GetPlansAsync(ct))).RequireAuthorization().WithTags("Subscriptions");
        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext context, ISubscriptionService service, CancellationToken ct) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
            var result = await service.SubscribeAsync(userId, context.User.Identity?.Name ?? email, email, request.ProductHandle, ct);
            return result.AlreadySubscribed ? Results.Ok(result) : Results.Created("api/my-subscriptions", result);
        }).RequireAuthorization().WithTags("Subscriptions");
        app.MapGet("api/my-subscriptions", async (HttpContext context, ISubscriptionService service, CancellationToken ct) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrWhiteSpace(userId) ? Results.Unauthorized() : Results.Ok(await service.GetMySubscriptionsAsync(userId, ct));
        }).RequireAuthorization().WithTags("Subscriptions");
    }
    public Task<IResult> HandleAsync(ISubscriptionService service) => Task.FromResult<IResult>(Results.NotFound());
}
