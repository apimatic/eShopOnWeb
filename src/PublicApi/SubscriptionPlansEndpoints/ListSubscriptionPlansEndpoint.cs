using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithRequest<ListSubscriptionPlansRequest>
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioService _maxioService;

    public ListSubscriptionPlansEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Lists all available subscription plans",
        OperationId = "subscriptionPlans.list",
        Tags = new[] { "SubscriptionPlansEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        ListSubscriptionPlansRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var products = await _maxioService.ListProductsAsync();
            response.Plans.AddRange(products.Where(p => p != null).Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                PriceInCents = p.PriceInCents
            }));

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to retrieve subscription plans", details = ex.Message });
        }
    }
}
