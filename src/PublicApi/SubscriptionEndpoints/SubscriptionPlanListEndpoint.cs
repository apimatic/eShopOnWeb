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
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : AuthenticatedBillingRequest
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

    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    /// <summary>True when the plan list hit its page cap and may be incomplete.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// List the subscription plans available (the products of the configured Maxio product family)
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var request = new ListSubscriptionPlansRequest
                {
                    UserName = context.User.Identity?.Name ?? string.Empty,
                    RequestAborted = cancellationToken
                };
                return await HandleAsync(request, billing);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces<BillingErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<BillingErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionBillingService billing)
    {
        try
        {
            var catalog = await billing.GetPlansAsync(request.RequestAborted);
            return Results.Ok(new ListSubscriptionPlansResponse(request.CorrelationId())
            {
                Plans = catalog.Plans.Select(SubscriptionPlanDto.From).ToList(),
                Truncated = catalog.Truncated
            });
        }
        catch (BillingException ex)
        {
            return BillingResults.From(ex, request.CorrelationId());
        }
    }
}
