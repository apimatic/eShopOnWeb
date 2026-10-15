using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Body of POST api/subscriptions. The caller is identified by the token only.</summary>
public class SubscribeBody
{
    public string? PlanHandle { get; set; }
}

public class SubscribeRequest : BaseRequest
{
    public string UserName { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public CancellationToken CancellationToken { get; init; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    /// <summary>Created, AlreadySubscribed or Pending (outcome not yet known; see GET api/my-subscriptions).</summary>
    public string Outcome { get; set; } = string.Empty;
    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>
/// Subscribes the signed-in user to a plan (idempotent per user and plan)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeBody body, ClaimsPrincipal user, ISubscriptionService service, CancellationToken cancellationToken) =>
            {
                var userName = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(userName)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(body?.PlanHandle))
                {
                    return Results.BadRequest(new { message = "planHandle is required." });
                }
                return await HandleAsync(new SubscribeRequest
                {
                    UserName = userName,
                    PlanHandle = body.PlanHandle.Trim(),
                    CancellationToken = cancellationToken
                }, service);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces<SubscribeResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService service)
    {
        var result = await service.SubscribeAsync(request.UserName, request.PlanHandle, request.CancellationToken);
        var response = new SubscribeResponse(request.CorrelationId())
        {
            Outcome = result.Outcome.ToString(),
            Subscription = result.Subscription == null ? null : SubscriptionMapper.ToDto(result.Subscription)
        };
        return result.Outcome switch
        {
            SubscribeOutcome.Created => Results.Created("api/my-subscriptions", response),
            SubscribeOutcome.AlreadySubscribed => Results.Ok(response),
            _ => Results.Accepted("api/my-subscriptions", response)
        };
    }
}

public static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(SubscriptionInfo s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents / 100m,
        State = s.State,
        NextBillingDate = s.NextBillingDate,
        DetailsAvailable = s.DetailsAvailable
    };
}
