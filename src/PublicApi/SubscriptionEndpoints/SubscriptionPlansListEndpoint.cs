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
/// Lists the subscription plans a shopper can subscribe to (GET /api/subscription-plans).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var catalog = await subscriptionService.GetCatalogAsync(CancellationToken.None);

        response.Plans = catalog.Plans.Select(plan => new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            PriceInCents = plan.PriceInCents,
            RequiresCreditCard = plan.RequiresCreditCard
        }).ToList();

        if (catalog.MeteredComponent is not null)
        {
            response.MeteredComponent = new UsageComponentDto
            {
                Handle = catalog.MeteredComponent.Handle,
                Kind = catalog.MeteredComponent.Kind,
                PricePerUnitInCents = catalog.MeteredComponent.PricePerUnitInCents
            };
        }

        return Results.Ok(response);
    }
}
