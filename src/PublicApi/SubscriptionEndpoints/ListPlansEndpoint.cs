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
/// List the subscribable recurring plans (UC1, step 1). Anonymous — pricing is public.
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await HandleAsync(subscriptionService, cancellationToken))
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
        => HandleAsync(subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var plans = await subscriptionService.ListPlansAsync(cancellationToken);

        var response = new ListPlansResponse();
        response.Plans.AddRange(plans.Select(SubscriptionPlanDto.From));

        return Results.Ok(response);
    }
}

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
