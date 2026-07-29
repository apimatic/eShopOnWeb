using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Empty request for listing subscription plans (identity comes from the JWT).</summary>
public class SubscriptionPlanListRequest : BaseRequest
{
}

/// <summary>Response carrying the available subscription plans.</summary>
public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlanListResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the subscription plans available to subscribe to (from the configured Maxio product family).
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, ISubscriptionAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionAppService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(), subscriptionService, cancellationToken);
            })
            .Produces<SubscriptionPlanListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionPlanListRequest request, ISubscriptionAppService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, ISubscriptionAppService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());
        var plans = await subscriptionService.GetPlansAsync(cancellationToken);
        response.Plans = plans.Select(p => p.ToDto()).ToList();
        return Results.Ok(response);
    }
}
