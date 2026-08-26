using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans available for signup (products in the configured Maxio product family)
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionBillingService billingService)
        => HandleAsync(billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await billingService.ListPlansAsync(cancellationToken);

        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle ?? string.Empty,
            Name = p.Name ?? string.Empty,
            Description = p.Description ?? string.Empty,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit ?? string.Empty,
            ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
        }));

        return Results.Ok(response);
    }
}
