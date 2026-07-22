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
/// Preview the prorated cost of moving a subscription to another plan
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangePreviewRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public PlanChangePreviewEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangePreviewRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.UserReference = SubscriptionUser.GetReference(user);
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangePreviewRequest request,
        ISubscriptionService subscriptionService)
    {
        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(request.UserReference,
            request.SubscriptionId, request.TargetPlanHandle);
        response.Preview = _mapper.Map<PlanChangePreviewDto>(preview);

        return Results.Ok(response);
    }
}
