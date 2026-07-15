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

/// <summary>Previews the cost impact of a plan change before it is committed (UC3).</summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PreviewPlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserId = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        var response = new PreviewPlanChangeResponse(request.CorrelationId());

        try
        {
            var preview = await subscriptionService.PreviewPlanChangeAsync(
                request.UserId, request.SubscriptionId, request.TargetProductHandle, request.ApplyImmediately);

            response.CurrentProductHandle = preview.CurrentProductHandle;
            response.TargetProductHandle = preview.TargetProductHandle;
            response.ApplyImmediately = preview.ApplyImmediately;
            response.ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents;
            response.ChargeInCents = preview.ChargeInCents;
            response.PaymentDueInCents = preview.PaymentDueInCents;
            response.CreditAppliedInCents = preview.CreditAppliedInCents;
            response.StalenessToken = preview.StalenessToken;
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
