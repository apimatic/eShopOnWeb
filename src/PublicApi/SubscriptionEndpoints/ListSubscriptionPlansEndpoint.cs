using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient, IConfiguration>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioAdvancedBillingClient client, IConfiguration config) =>
            {
                return await HandleAsync(client, config);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient client, IConfiguration config)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var productFamilyHandle = config["Maxio:ProductFamilyHandle"];
            if (string.IsNullOrEmpty(productFamilyHandle))
                return Results.BadRequest("Maxio ProductFamilyHandle not configured");

            var products = await client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: productFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: default);

            foreach (var productResponse in products)
            {
                if (productResponse.Product != null)
                {
                    response.Plans.Add(new PlanDto
                    {
                        Id = productResponse.Product.Id,
                        Handle = productResponse.Product.Handle,
                        Name = productResponse.Product.Name,
                        PriceInCents = productResponse.Product.PriceInCents ?? 0
                    });
                }
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
}
