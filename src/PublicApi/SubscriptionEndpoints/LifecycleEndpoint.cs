using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}

/// <summary>
/// One management surface for the four lifecycle actions (UC4). A customer may only act
/// on their own subscription; an administrator may act on any (ownership is enforced by
/// SubscriptionService).
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserId = user.Identity!.Name!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = request.Action switch
        {
            LifecycleAction.Pause => await subscriptionService.PauseAsync(request.UserId, request.IsAdmin, request.SubscriptionId),
            LifecycleAction.Resume => await subscriptionService.ResumeAsync(request.UserId, request.IsAdmin, request.SubscriptionId),
            LifecycleAction.Cancel => await subscriptionService.CancelAsync(request.UserId, request.IsAdmin, request.SubscriptionId, request.EndOfPeriod, request.Reason),
            LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(request.UserId, request.IsAdmin, request.SubscriptionId),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Action), request.Action, "Unknown lifecycle action.")
        };

        response.Subscription = SubscriptionDto.FromSubscription(subscription);
        return Results.Ok(response);
    }
}

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public LifecycleAction Action { get; set; }
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }

    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsAdmin { get; set; }
}

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
