using System.Security.Claims;
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

/// <summary>Commits a plan change previously shown via PreviewPlanChangeEndpoint (UC3).</summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/commit",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, CommitPlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserId = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        var response = new CommitPlanChangeResponse(request.CorrelationId());

        try
        {
            var subscription = await subscriptionService.CommitPlanChangeAsync(
                request.UserId, request.SubscriptionId, request.TargetProductHandle, request.ApplyImmediately, request.StalenessToken);

            response.Subscription = SubscriptionEndpointMappers.ToDto(subscription);
        }
        catch (PlanChangePreviewStaleException ex)
        {
            return Results.Conflict(ex.Message);
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
        catch (BillingConfigurationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Target plan is not configured");
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider error");
        }

        return Results.Ok(response);
    }
}
