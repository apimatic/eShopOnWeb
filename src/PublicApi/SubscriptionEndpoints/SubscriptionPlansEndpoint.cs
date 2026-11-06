using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public IReadOnlyList<PlanDto> Plans { get; set; } = Array.Empty<PlanDto>();

    /// <summary>True when the plan list is incomplete because the page cap was hit.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Lists the subscription plans available to subscribe to
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, MaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioSubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), service, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, MaxioSubscriptionService service, CancellationToken ct)
    {
        try
        {
            var listing = await service.ListPlansAsync(ct);
            return Results.Ok(new ListSubscriptionPlansResponse(request.CorrelationId())
            {
                Plans = listing.Plans,
                Truncated = listing.Truncated
            });
        }
        catch (BillingException ex)
        {
            return ex.ToResult();
        }
    }
}
