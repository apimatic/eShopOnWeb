using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _maxioSettings;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(IMaxioClient maxioClient, MaxioSettings maxioSettings, ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _maxioSettings = maxioSettings;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioClient maxioClient, MaxioSettings settings, ILogger<ListSubscriptionPlansEndpoint> logger) =>
            {
                return await HandleAsync(maxioClient, settings, logger);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("GetSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(IMaxioClient maxioClient, MaxioSettings settings, ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        try
        {
            var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
            var productFamilyHandle = settings.ProductFamilyHandle ?? "eshop-subscribe";
            var products = await maxioClient.GetProductsForFamilyAsync(productFamilyHandle);

            response.Plans.AddRange(products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle ?? "",
                Name = p.Name ?? "",
                Price = p.PriceInCents / 100m,
                BillingCycle = $"${p.PriceInCents / 100m:F2} per {p.IntervalUnit ?? "month"}"
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing subscription plans");
            return Results.StatusCode(500);
        }
    }
}
