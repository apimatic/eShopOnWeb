using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscription plans
/// </summary>
public partial class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SubscriptionPlansEndpoint> _logger;

    public SubscriptionPlansEndpoint(MaxioAdvancedBillingClient client, IConfiguration configuration, ILogger<SubscriptionPlansEndpoint> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
        var maxioSettings = _configuration.GetSection("Maxio").Get<MaxioSettings>();

        if (maxioSettings?.ProductFamilyHandle == null)
        {
            _logger.LogError("Maxio ProductFamilyHandle not configured");
            return Results.BadRequest("Subscription plans not available");
        }

        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: maxioSettings.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: CancellationToken.None);

            foreach (var productResponse in products)
            {
                var product = productResponse?.Product;
                if (product?.Id.HasValue == true)
                {
                    response.Plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id.Value,
                        Name = product.Name ?? "",
                        Handle = product.Handle ?? "",
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? ""
                    });
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching subscription plans");
            return Results.StatusCode(500);
        }
    }
}
