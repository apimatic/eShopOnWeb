using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient, IOptions<MaxioConfiguration>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioAdvancedBillingClient client, IOptions<MaxioConfiguration> cfg) =>
            {
                return await HandleAsync(client, cfg);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient client, IOptions<MaxioConfiguration> cfg)
    {
        try
        {
            var handle = cfg.Value.ProductFamilyHandle;
            var familyResponse = await client.ProductFamilies.ReadProductFamily($"handle:{handle}");
            var family = familyResponse.ProductFamily;
            // List products; we'll include all and filter by family id if available
            // Per contract, ListProductsFilter may filter by family; if not available pass null
            var products = await client.Products.ListProducts(filter: null, page: 1, perPage: 20);
            var response = new ListSubscriptionPlansResponse();
            foreach (var p in products)
            {
                var prod = p.Product;
                if (prod == null) continue;
                response.Plans.Add(new SubscriptionPlanDto
                {
                    Handle = prod.ApiHandle ?? prod.Id?.ToString() ?? string.Empty,
                    Name = prod.Name ?? string.Empty,
                    Price = prod.PriceInCents.HasValue ? prod.PriceInCents.Value / 100m : 0,
                    Interval = "month",
                    Id = prod.Id ?? 0
                });
            }
            return Results.Ok(response);
        }
        catch (SdkException<MaxioAdvancedBilling.Core.Exceptions.RawError> ex)
        {
            return Results.Problem(detail: ex.Error?.ReadAsString() ?? "Maxio error", statusCode: 502);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Interval { get; set; } = string.Empty;
    public int Id { get; set; }
}
