using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for purchase.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _billingService;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List Subscription Plans",
        Description = "Lists the subscription plans available for purchase",
        OperationId = "subscription-plans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _billingService.ListPlansAsync(cancellationToken);

        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Price = FormatPrice(p.PriceInCents),
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequiresPaymentMethod = p.RequiresPaymentMethod,
                ProductFamilyHandle = p.ProductFamilyHandle
            }).ToList()
        };

        return Ok(response);
    }

    internal static string FormatPrice(long priceInCents)
    {
        return (priceInCents / 100.0m).ToString("0.00");
    }
}
