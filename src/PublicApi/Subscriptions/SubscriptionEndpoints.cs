using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEndpoints : IEndpoint<IResult, MaxioSubscriptionService>
{
    public Task<IResult> HandleAsync(MaxioSubscriptionService service) => Task.FromResult(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        var plans = app.MapGet("api/subscription-plans", async (MaxioSubscriptionService service, CancellationToken ct) =>
            Results.Ok(await service.GetPlansAsync(ct)));
        var subscribe = app.MapPost("api/subscriptions", async (SubscribeRequest request, ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken ct) =>
        {
            var name = user.Identity?.Name;
            if (string.IsNullOrWhiteSpace(name)) return Results.Unauthorized();
            try { return Results.Ok(await service.SubscribeAsync(name, request.PlanHandle, ct)); }
            catch (MaxioRequestException ex) { return Results.Problem(ex.Message, statusCode: (int)ex.Status); }
            catch (MaxioUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 502); }
        });
        var mine = app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken ct) =>
        {
            var name = user.Identity?.Name;
            if (string.IsNullOrWhiteSpace(name)) return Results.Unauthorized();
            try { return Results.Ok(await service.GetMySubscriptionsAsync(name, ct)); }
            catch (MaxioUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 502); }
        });
        plans.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        subscribe.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        mine.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }
}
