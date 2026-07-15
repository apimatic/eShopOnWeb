using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One management surface, four lifecycle actions (UC4): pause / resume / cancel / reactivate. Any
/// authenticated user may act on their own subscription; a member of the Administrators role may act on
/// any subscription.
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserId = user.Identity?.Name;
                request.ActingAsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        var response = new LifecycleResponse(request.CorrelationId());

        try
        {
            Subscription subscription = request.Action switch
            {
                LifecycleAction.Pause => await subscriptionService.PauseAsync(request.UserId, request.ActingAsAdmin, request.SubscriptionId),
                LifecycleAction.Resume => await subscriptionService.ResumeAsync(request.UserId, request.ActingAsAdmin, request.SubscriptionId),
                LifecycleAction.Cancel => await subscriptionService.CancelAsync(request.UserId, request.ActingAsAdmin, request.SubscriptionId, request.EndOfPeriod, request.Reason),
                LifecycleAction.Reactivate => await subscriptionService.ReactivateAsync(request.UserId, request.ActingAsAdmin, request.SubscriptionId),
                _ => throw new System.ArgumentOutOfRangeException(nameof(request.Action), request.Action, "Unknown lifecycle action")
            };

            response.Subscription = SubscriptionEndpointMappers.ToDto(subscription);
        }
        catch (System.ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (SubscriptionNotFoundException ex)
        {
            return Results.NotFound(ex.Message);
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            return Results.Conflict(ex.Message);
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider error");
        }

        return Results.Ok(response);
    }
}
