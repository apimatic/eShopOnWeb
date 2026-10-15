using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET /api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}

/// <summary>
/// Subscribes the authenticated shopper to a plan (idempotent per shopper + plan)
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, SubscriptionService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, service, user, ct);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service, ClaimsPrincipal user, CancellationToken ct)
    {
        // Identity comes from the token, never from the body.
        var (subscription, created) = await service.SubscribeAsync(user.Identity?.Name, request?.PlanHandle, ct);
        var response = new SubscribeResponse(request!.CorrelationId()) { Subscription = subscription };
        return created ? Results.Created("api/my-subscriptions", response) : Results.Ok(response);
    }
}
