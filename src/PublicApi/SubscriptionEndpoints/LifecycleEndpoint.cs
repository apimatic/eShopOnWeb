using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One surface for the four lifecycle actions — pause, resume, cancel, reactivate (UC4).
/// </summary>
public class LifecycleEndpoint : SubscriptionEndpointBase,
    IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    private const string PauseAction = "pause";
    private const string ResumeAction = "resume";
    private const string CancelAction = "cancel";
    private const string ReactivateAction = "reactivate";

    public LifecycleEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        var userReference = ResolveUserReference(request.UserReference);
        if (userReference is null)
        {
            return Denied();
        }

        var action = request.Action?.Trim().ToLowerInvariant();

        var response = new LifecycleResponse(request.CorrelationId());

        BillingSubscription subscription;

        switch (action)
        {
            case PauseAction:
                subscription = await subscriptionService.PauseAsync(userReference,
                    request.AutomaticallyResumeAt);
                break;

            case ResumeAction:
                subscription = await subscriptionService.ResumeAsync(userReference);
                break;

            case CancelAction:
                var timing = request.CancelAtEndOfPeriod
                    ? CancellationTiming.EndOfBillingPeriod
                    : CancellationTiming.Immediate;
                subscription = await subscriptionService.CancelAsync(userReference, timing, request.Reason);
                break;

            case ReactivateAction:
                subscription = await subscriptionService.ReactivateAsync(userReference);
                break;

            default:
                return Results.BadRequest(
                    $"Unknown action '{request.Action}'. Use 'pause', 'resume', 'cancel' or 'reactivate'.");
        }

        response.Action = action;
        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
