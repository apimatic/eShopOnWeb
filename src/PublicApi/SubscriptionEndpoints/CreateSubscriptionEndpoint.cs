using System;
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

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user + plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService service, CancellationToken ct) =>
                await HandleAsync(request, user, service, ct))
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService service, CancellationToken ct)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
            return Results.Unauthorized();

        var (subscription, created) = await service.SubscribeAsync(userName, request.PlanHandle, ct);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription,
            AlreadySubscribed = !created
        };
        return created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
    /// <summary>True when the user already had this subscription and no new one was created.</summary>
    public bool AlreadySubscribed { get; set; }
}
