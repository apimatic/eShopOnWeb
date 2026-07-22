using System.Security.Claims;
using System.Threading;
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
/// Previews and commits a subscription plan change. Preview first, then send the previewed AmountDue back
/// as ConfirmedAmountDue — the commit is refused if the provider would now charge something else.
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
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                Bind(request, subscriptionId, user);

                return await HandlePreviewAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                Bind(request, subscriptionId, user);

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandlePreviewAsync(PlanChangeRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        return await SubscriptionErrorResults.ExecuteAsync(async () =>
        {
            var preview = await subscriptionService.PreviewPlanChangeAsync(
                request.SubscriptionId,
                request.TargetPlanHandle,
                request.Timing,
                request.OwnerReference,
                cancellationToken);

            var response = new PlanChangePreviewResponse(request.CorrelationId())
            {
                Preview = _mapper.Map<PlanChangePreviewDto>(preview)
            };

            return Results.Ok(response);
        });
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        return await SubscriptionErrorResults.ExecuteAsync(async () =>
        {
            var result = await subscriptionService.ChangePlanAsync(
                request.SubscriptionId,
                request.TargetPlanHandle,
                request.Timing,
                request.ConfirmedAmountDue,
                request.OwnerReference,
                cancellationToken);

            var response = new PlanChangeResponse(request.CorrelationId())
            {
                Result = _mapper.Map<PlanChangeResultDto>(result)
            };

            return Results.Ok(response);
        });
    }

    private static void Bind(PlanChangeRequest request, int subscriptionId, ClaimsPrincipal user)
    {
        request.SubscriptionId = subscriptionId;
        request.OwnerReference = SubscriptionCaller.ResolveOwnerReference(user);
        request.IsAuthenticated = user.Identity?.Name is not null;
    }

    private static IResult? Validate(PlanChangeRequest request)
    {
        if (!request.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest(new { error = "targetPlanHandle is required." });
        }

        return null;
    }
}
