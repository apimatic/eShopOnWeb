using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<SubscriptionPlanDto>>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IConfiguration _configuration;

    public SubscriptionPlansEndpoint(MaxioAdvancedBillingClient maxioClient, IConfiguration configuration)
    {
        _maxioClient = maxioClient;
        _configuration = configuration;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Retrieves the list of available subscription plans from Maxio",
        OperationId = "subscriptions.getPlans",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<List<SubscriptionPlanDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var productFamilyHandle = _configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
            var plans = new List<SubscriptionPlanDto>();

            // List products with pagination support
            int page = 1;
            int perPage = 20;
            bool hasMore = true;

            while (hasMore)
            {
                var response = await _maxioClient.Products.ListProducts(
                    dateField: null,
                    filter: null,
                    endDate: null,
                    endDatetime: null,
                    startDate: null,
                    startDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: cancellationToken);

                if (response == null || response.Count == 0)
                {
                    hasMore = false;
                    break;
                }

                foreach (var productResponse in response)
                {
                    if (productResponse?.Product?.Handle != null)
                    {
                        // Read full product details by handle to get complete info
                        try
                        {
                            var fullProduct = await _maxioClient.Products.ReadProductByHandle(
                                apiHandle: productResponse.Product.Handle,
                                ct: cancellationToken);

                            var product = fullProduct?.Product;
                            if (product != null && product.PriceInCents.HasValue)
                            {
                                plans.Add(new SubscriptionPlanDto(
                                    Handle: product.Handle ?? "",
                                    Name: product.Name ?? "Unnamed Plan",
                                    Description: product.Description,
                                    PricePerMonth: product.PriceInCents.Value / 100m,
                                    BillingInterval: $"Every {product.Interval} {product.IntervalUnit?.Value ?? "month"}"
                                ));
                            }
                        }
                        catch
                        {
                            // Skip products that fail to load detailed information
                        }
                    }
                }

                if (response.Count < perPage)
                {
                    hasMore = false;
                }
                else
                {
                    page++;
                }
            }

            return Ok(plans);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to retrieve subscription plans: {ex.Message}");
        }
    }
}
