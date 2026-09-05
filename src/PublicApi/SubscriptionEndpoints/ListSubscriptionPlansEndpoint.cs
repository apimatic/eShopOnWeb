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

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        IConfiguration configuration,
        ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _configuration = configuration;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () => await HandleAsync())
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("GetSubscriptionPlans")
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();
            var productFamilyHandle = _configuration["Maxio:ProductFamilyHandle"];

            if (string.IsNullOrEmpty(productFamilyHandle))
            {
                _logger.LogError("Maxio:ProductFamilyHandle is not configured");
                return Results.BadRequest(new { error = "Product family handle not configured" });
            }

            var products = await _maxioClient.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 100,
                ct: default);

            if (products == null)
            {
                return Results.Ok(response);
            }

            foreach (var productResponse in products)
            {
                if (productResponse.Product?.ProductFamily?.Handle == productFamilyHandle)
                {
                    var plan = new SubscriptionPlanDto
                    {
                        Handle = productResponse.Product?.Handle,
                        Name = productResponse.Product?.Name,
                        Description = productResponse.Product?.Description,
                        PriceInCents = productResponse.Product?.PriceInCents,
                        Interval = productResponse.Product?.Interval,
                        IntervalUnit = productResponse.Product?.IntervalUnit?.ToString()
                    };
                    response.Plans.Add(plan);
                }
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
