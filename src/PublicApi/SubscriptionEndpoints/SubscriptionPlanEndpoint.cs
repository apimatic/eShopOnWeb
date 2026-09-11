using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionPlanEndpoint : EndpointBaseAsync
    .WithRequest<object>
    .WithActionResult<System.Collections.Generic.List<SubscriptionPlanResponse>>
{
    private readonly MaxioSubscriptionService _service;
    public SubscriptionPlanEndpoint(MaxioSubscriptionService service) => _service = service;

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List subscription plans", OperationId = "subscription.plans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<System.Collections.Generic.List<SubscriptionPlanResponse>>> HandleAsync(object request, CancellationToken cancellationToken = default)
    {
        var products = await _service.GetPlanProductsAsync(cancellationToken);
        var result = products.Select(p => new SubscriptionPlanResponse
        {
            Id = p.Product?.Id ?? 0,
            Handle = p.Product?.Handle ?? "",
            Name = p.Product?.Name ?? "",
            PricePerMonth = (p.Product?.PriceInCents ?? 0) / 100m,
            Currency = "USD"
        }).ToList();
        return Ok(result);
    }
}
