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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists subscription plans in the configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                return await ListAsync(billing, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionPlanEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
        => ListAsync(billing, CancellationToken.None);

    private static async Task<IResult> ListAsync(ISubscriptionBillingService billing, CancellationToken ct)
    {
        var response = new ListSubscriptionPlansResponse();
        var plans = await billing.ListPlansAsync(ct);
        response.Plans.AddRange(plans.Select(SubscriptionPlanDtoMapper.From));
        return Results.Ok(response);
    }
}
