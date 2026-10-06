using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for purchase.
/// </summary>
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly MaxioOptions _options;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _subscriptionService = subscriptionService;
        _options = options.Value;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the subscription plans configured in the Maxio product family for eShopOnWeb",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse()
        {
            ProductFamilyHandle = _options.ProductFamilyHandle
        };

        var plans = await _subscriptionService.GetPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle ?? string.Empty,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Price = FormatPrice(p.PriceInCents),
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequireCreditCard = p.RequireCreditCard,
        }));

        return Ok(response);
    }

    internal static string FormatPrice(long priceInCents) => (priceInCents / 100m).ToString("0.00");
}