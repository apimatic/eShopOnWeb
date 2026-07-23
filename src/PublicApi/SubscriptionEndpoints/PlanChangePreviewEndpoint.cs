using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Preview the prorated cost of moving a subscription to another plan (UC3 step 2). Nothing is
/// charged; the returned token must be echoed back to commit the change.
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public PlanChangePreviewEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change-preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionCaller.ResolveOwnerScope(user);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(
            request.SubscriptionId, request.OwnerReference, request.TargetPlanHandle, request.ResolveTiming());

        response.Preview = _mapper.Map<PlanChangePreviewDto>(preview);

        return Results.Ok(response);
    }
}
