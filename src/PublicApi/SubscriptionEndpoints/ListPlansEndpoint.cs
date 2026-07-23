using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to enrol in (UC1, step 1).
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ListPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await HandleAsync(new ListPlansRequest(cancellationToken), subscriptionService))
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        var plans = await subscriptionService.GetAvailablePlansAsync(request.CancellationToken);
        response.Plans.AddRange(plans.Select(PlanDto.FromPlan));

        return Results.Ok(response);
    }
}

public class ListPlansRequest : BaseRequest
{
    public ListPlansRequest(CancellationToken cancellationToken) => CancellationToken = cancellationToken;

    public CancellationToken CancellationToken { get; }
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPlansResponse()
    {
    }

    public List<PlanDto> Plans { get; set; } = new();
}
