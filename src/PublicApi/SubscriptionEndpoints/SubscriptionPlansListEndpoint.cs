using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// List available subscription plans.
/// GET /api/subscription-plans (JWT authenticated).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken ct) =>
                await HandleAsync(billing, ct))
            .Produces<IEnumerable<SubscriptionPlanDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing, CancellationToken ct)
    {
        try
        {
            var plans = await billing.GetPlansAsync(ct);
            return Results.Ok(plans.Select(SubscriptionApiSupport.ToDto).ToList());
        }
        catch (BillingException ex)
        {
            return SubscriptionApiSupport.ToErrorResult(ex);
        }
    }
}
