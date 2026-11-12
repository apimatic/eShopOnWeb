using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request, HttpContext http) =>
            {
                return await HandleAsync(request ?? new SubscribeRequest(), http);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext http)
    {
        var userId = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var plan = request.PlanHandle ?? request.ProductHandle;
        var service = http.RequestServices.GetRequiredService<MaxioSubscriptionService>();
        var result = await service.SubscribeAsync(userId, userId, plan, http.RequestAborted);
        var response = new SubscribeResponse { Subscription = result.Subscription, AlreadySubscribed = !result.Created };
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}

public class SubscribeRequest
{
    /// <summary>Handle of the plan to subscribe to (one of GET /api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Alias of PlanHandle.</summary>
    public string? ProductHandle { get; set; }
}

public class SubscribeResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
    public bool AlreadySubscribed { get; set; }
}
