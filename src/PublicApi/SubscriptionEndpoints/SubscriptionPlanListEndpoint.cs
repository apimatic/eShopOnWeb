using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    /// <summary>True when the plan list was cut short by a page cap and may be incomplete.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// List the subscription plans that can be subscribed to
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptions, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptions) =>
        HandleAsync(subscriptions, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptions, CancellationToken cancellationToken)
    {
        var plans = await subscriptions.ListPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Plans.Select(SubscriptionPlanDto.From).ToList(),
            Truncated = plans.Truncated,
        };
        return Results.Ok(response);
    }
}
