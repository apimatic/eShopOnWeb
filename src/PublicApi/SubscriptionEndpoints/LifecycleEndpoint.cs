using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC4 — one surface for the four lifecycle actions: pause / resume / cancel (immediate or
/// end-of-period) / reactivate. JWT-secured.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        var subscription = action switch
        {
            "pause" => await subscriptionService.PauseAsync(request.SubscriptionId),
            "resume" => await subscriptionService.ResumeAsync(request.SubscriptionId),
            "cancel" => await subscriptionService.CancelAsync(request.SubscriptionId, request.Immediate, request.Reason),
            "reactivate" => await subscriptionService.ReactivateAsync(request.SubscriptionId),
            _ => throw new BillingProviderException(
                $"Unknown lifecycle action '{request.Action}'. Expected one of: pause, resume, cancel, reactivate.")
        };

        return Results.Ok(new LifecycleResponse(request.CorrelationId()) { Subscription = subscription.ToDto() });
    }
}

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>One of: pause, resume, cancel, reactivate.</summary>
    public string? Action { get; set; }

    /// <summary>For cancel: true = immediate, false = end-of-period.</summary>
    public bool Immediate { get; set; } = true;

    public string? Reason { get; set; }
}

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId) { }

    public LifecycleResponse() { }

    public CustomerSubscriptionDto Subscription { get; set; } = new();
}
