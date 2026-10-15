using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans offered by the configured billing product family
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces<ErrorDetails>(StatusCodes.Status502BadGateway)
            .Produces<ErrorDetails>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) => HandleAsync(billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var catalog = await billing.ListPlansAsync(cancellationToken);

        response.Plans.AddRange(catalog.Plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Price = p.PriceInCents / 100m,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequiresPaymentMethod = p.RequiresPaymentMethod
        }));
        response.IsTruncated = catalog.IsTruncated;

        return Results.Ok(response);
    }
}
