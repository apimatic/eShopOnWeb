using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for <see cref="CreateSubscriptionEndpoint"/>. The caller identity is taken from the JWT,
/// never the body, so those fields are not serialized/bindable.
/// </summary>
public class SubscribeSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to. Optional; when omitted the default plan is used.</summary>
    public string? PlanHandle { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Caller { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}

public class SubscribeSubscriptionResponse : BaseResponse
{
    public SubscribeSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeSubscriptionResponse() { }

    public CustomerSubscriptionDto Subscription { get; set; } = new();
}

/// <summary>
/// Subscribes the authenticated caller to a plan. Idempotent (a repeated request does not create a
/// second customer or duplicate subscription). JWT-authenticated.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                request.Caller = user;
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var subscribeRequest = request.Caller!.ToSubscribeRequest(request.PlanHandle);
        var subscription = await billingService.SubscribeAsync(subscribeRequest, request.CancellationToken);
        var response = new SubscribeSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        };
        return Results.Ok(response);
    }
}
