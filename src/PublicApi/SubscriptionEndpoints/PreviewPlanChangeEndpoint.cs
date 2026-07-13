using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC3 step 2: preview a plan change before committing.</summary>
public class PreviewPlanChangeEndpoint : IEndpoint<IResult, PreviewPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, PreviewPlanChangeBody body, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var customerReference = httpContext.User.Identity?.Name ?? string.Empty;
                var actingAsAdmin = httpContext.User.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new PreviewPlanChangeRequest(subscriptionId, customerReference, actingAsAdmin, body);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PreviewPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PreviewPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PreviewPlanChangeResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(
            request.CustomerReference, request.ActingAsAdmin, request.SubscriptionId, request.TargetProductHandle, request.Timing);

        response.Preview = PlanChangePreviewDto.FromModel(preview);
        return Results.Ok(response);
    }
}
