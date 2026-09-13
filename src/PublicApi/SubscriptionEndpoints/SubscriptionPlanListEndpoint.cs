using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public SubscriptionPlanListEndpoint(IMaxioClient maxioClient, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists subscription plans from the configured Maxio product family",
        OperationId = "subscription.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();
        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);

        response.Plans = products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                Price = p.Price,
                IntervalUnit = p.IntervalUnit,
                Interval = p.Interval,
                RequireCreditCard = p.RequireCreditCard
            })
            .ToList();

        return Ok(response);
    }
}
