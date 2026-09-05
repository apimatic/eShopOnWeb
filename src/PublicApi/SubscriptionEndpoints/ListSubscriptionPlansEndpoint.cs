using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to eShopOnWeb customers, sourced live from the
/// Maxio Advanced Billing product family configured via Maxio:ProductFamilyHandle.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService maxioService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), maxioService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioSubscriptionService maxioService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await maxioService.GetPlansAsync();
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.PriceInCents / 100m,
            BillingInterval = FormatInterval(p.Interval, p.IntervalUnit)
        }).ToList();

        return Results.Ok(response);
    }

    private static string FormatInterval(int interval, string intervalUnit)
    {
        var unit = intervalUnit.TrimEnd('s');
        return interval == 1 ? $"every {unit}" : $"every {interval} {unit}s";
    }
}
