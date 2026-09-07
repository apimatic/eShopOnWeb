using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioApiService _maxioApiService;
    private readonly MaxioSettings _maxioSettings;

    public ListSubscriptionPlansEndpoint(
        IMaxioApiService maxioApiService,
        MaxioSettings maxioSettings)
    {
        _maxioApiService = maxioApiService;
        _maxioSettings = maxioSettings;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Get all available subscription plans from the product family",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "Subscriptions" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_maxioSettings.ProductFamilyHandle))
        {
            return BadRequest(new { error = "Maxio configuration incomplete" });
        }

        var products = await _maxioApiService.GetProductsByFamilyHandleAsync(_maxioSettings.ProductFamilyHandle);
        var plans = new List<SubscriptionPlanDto>();

        foreach (var product in products)
        {
            plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id,
                Handle = product.Handle,
                Name = product.Name,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Price = product.PriceInCents / 100m,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit
            });
        }

        return new ListSubscriptionPlansResponse { Plans = plans };
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}
