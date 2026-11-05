using System.Security.Claims;
using System.Text.Json.Serialization;
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
    /// <summary>Handle of the plan to subscribe to; must be one returned by GET api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }

    [JsonIgnore]
    public string? UserName { get; set; }

    [JsonIgnore]
    public CancellationToken Cancellation { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>False when the caller was already subscribed to the plan (the existing subscription is returned).</summary>
    public bool Created { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            {
                request.UserName = SubscriptionEndpointSupport.UserNameOf(user);
                request.Cancellation = ct;
                return await HandleAsync(request, service);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var (subscription, created) = await service.SubscribeAsync(request.UserName, request.PlanHandle, request.Cancellation);
            var response = new SubscribeResponse(request.CorrelationId()) { Subscription = subscription, Created = created };
            return created ? Results.Created($"api/my-subscriptions", response) : Results.Ok(response);
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionEndpointSupport.Failure(ex);
        }
    }
}
