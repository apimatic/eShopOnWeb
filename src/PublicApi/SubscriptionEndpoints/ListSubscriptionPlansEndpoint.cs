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

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionService, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, CancellationToken ct = default)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionService.ListPlansAsync(ct);

        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Price = p.PriceInCents / 100m,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            Taxable = p.Taxable,
            RequiresPaymentMethod = p.RequiresPaymentMethod
        }));

        return Results.Ok(response);
    }
}
