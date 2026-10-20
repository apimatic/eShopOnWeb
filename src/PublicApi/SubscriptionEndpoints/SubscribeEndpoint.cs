using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to. Defaults to "eshop-pro" when omitted.</summary>
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>False when the caller was already subscribed to the plan (idempotent repeat).</summary>
    public bool Created { get; set; }
}

/// <summary>
/// Subscribes the signed-in user to a plan (idempotent per user)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionApi>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, SubscriptionApi api) =>
            {
                return await HandleAsync(request, api);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionApi api)
    {
        var response = new SubscribeResponse(request.CorrelationId());
        var (subscription, created) = await api.SubscribeAsync(request.PlanHandle);
        response.Subscription = subscription;
        response.Created = created;
        return created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
