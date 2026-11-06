using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Filled from the JWT by the endpoint; never read from the body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string UserName { get; set; } = string.Empty;
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>False when the caller already had this subscription (repeat/double-click).</summary>
    public bool Created { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, MaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken ct) =>
            {
                request.UserName = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, service, ct);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, MaxioSubscriptionService service, CancellationToken ct)
    {
        try
        {
            var outcome = await service.SubscribeAsync(request.UserName, request.PlanHandle ?? string.Empty, ct);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = outcome.Subscription,
                Created = outcome.Created
            };
            return outcome.Created ? Results.Created("api/my-subscriptions", response) : Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return ex.ToResult();
        }
    }
}
