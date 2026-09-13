using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List Subscription Plans
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioApiClient maxio) =>
            {
                return await HandleAsync(maxio);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioApiClient maxio)
    {
        var response = new ListSubscriptionPlansResponse();
        var products = await maxio.ListProductsAsync();

        response.Plans = products.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequireCreditCard = p.RequireCreditCard,
            Taxable = p.Taxable,
            FamilyHandle = p.ProductFamily?.Handle,
            FamilyName = p.ProductFamily?.Name
        }).ToList();

        return Results.Ok(response);
    }
}
