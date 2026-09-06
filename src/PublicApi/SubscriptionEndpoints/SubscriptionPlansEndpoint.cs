using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionPlansEndpoint.Dependencies>
{
    public sealed class Dependencies
    {
        public Dependencies(MaxioAdvancedBillingClient maxioClient, IConfiguration configuration, ILogger<SubscriptionPlansEndpoint> logger)
        {
            MaxioClient = maxioClient;
            Configuration = configuration;
            Logger = logger;
        }

        public MaxioAdvancedBillingClient MaxioClient { get; }
        public IConfiguration Configuration { get; }
        public ILogger<SubscriptionPlansEndpoint> Logger { get; }
    }

    public sealed class ListSubscriptionPlansResponse
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (Dependencies deps) =>
            {
                return await HandleAsync(deps);
            })
           .RequireAuthorization()
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(Dependencies deps)
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();
            var productFamilyHandle = deps.Configuration["Maxio:ProductFamilyHandle"];

            deps.Logger.LogInformation("Fetching subscription plans from Maxio");

            var products = await deps.MaxioClient.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: default);

            var planHandles = new[] { "eshop-pro", "basic-plan" };

            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product?.Handle != null && planHandles.Contains(product.Handle))
                {
                    response.Plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle,
                        Name = product.Name,
                        Description = product.Description,
                        PriceInCents = product.PriceInCents,
                        Interval = product.Interval,
                        IntervalUnit = product.IntervalUnit?.ToString()
                    });
                }
            }

            deps.Logger.LogInformation("Successfully fetched {Count} subscription plans", response.Plans.Count);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            deps.Logger.LogError(ex, "Error fetching subscription plans");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
