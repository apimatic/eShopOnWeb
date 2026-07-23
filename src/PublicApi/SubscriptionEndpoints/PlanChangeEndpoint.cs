using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
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
/// Commit a plan change (UC3 step 4). The preview token the customer confirmed must still match a
/// fresh preview, otherwise the commit is rejected rather than applied at a different amount.
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public PlanChangeEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionCaller.ResolveOwnerScope(user);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewToken))
        {
            throw new InvalidSubscriptionOperationException(
                "A plan change must be confirmed against a preview: supply the preview's Token as previewToken.");
        }

        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.ChangePlanAsync(
            request.SubscriptionId,
            request.OwnerReference,
            request.TargetPlanHandle,
            request.ResolveTiming(),
            request.PreviewToken);

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
