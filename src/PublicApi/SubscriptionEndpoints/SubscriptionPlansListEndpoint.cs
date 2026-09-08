using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Maxio.Configuration;
using Microsoft.eShopWeb.Maxio.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) available in the configured product family.
/// </summary>
public class SubscriptionPlansListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListResponse>
{
    private readonly IMaxioBillingService _billingService;
    private readonly MaxioOptions _options;

    public SubscriptionPlansListEndpoint(IMaxioBillingService billingService, MaxioOptions options)
    {
        _billingService = billingService;
        _options = options;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the subscription plans a shopper can subscribe to (from the configured Maxio product family).",
        OperationId = "subscription-plans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansListResponse
        {
            ProductFamilyHandle = _options.ProductFamilyHandle
        };

        var plans = await _billingService.GetPlansAsync(cancellationToken);
        foreach (var plan in plans)
        {
            response.Plans.Add(SubscriptionDtoMapper.ToDto(plan));
        }

        return response;
    }
}
