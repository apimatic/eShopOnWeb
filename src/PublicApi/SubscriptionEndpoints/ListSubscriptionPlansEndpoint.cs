using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<SubscriptionPlansResponse>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;

    public ListSubscriptionPlansEndpoint(MaxioAdvancedBillingClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Retrieves all available subscription plans from the billing system",
        OperationId = "subscriptions.list_plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse();

        try
        {
            var products = await _maxioClient.Products.ListProducts(
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
                ct: cancellationToken);

            foreach (var product in products)
            {
                if (product.Product == null)
                    continue;

                response.Plans.Add(new SubscriptionPlan
                {
                    Id = product.Product.Id ?? 0,
                    Name = product.Product.Name ?? string.Empty,
                    Handle = product.Product.Handle ?? string.Empty,
                    PriceInCents = product.Product.PriceInCents ?? 0,
                    Interval = product.Product.Interval ?? 0,
                    IntervalUnit = product.Product.IntervalUnit?.ToString() ?? string.Empty
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to retrieve subscription plans", details = ex.Message });
        }
    }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlan> Plans { get; set; } = new();
}
