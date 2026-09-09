using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) offered in the configured product family.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _maxioOptions;

    public SubscriptionPlanListEndpoint(IMaxioClient maxioClient, IOptions<MaxioOptions> maxioOptions)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions.Value;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the Maxio products in the configured product family",
        OperationId = "subscription-plans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();

        IReadOnlyList<MaxioProduct> products;
        try
        {
            products = await _maxioClient.ListProductsAsync(cancellationToken);
        }
        catch (MaxioConfigurationException ex)
        {
            return ex.ToActionResult();
        }
        catch (MaxioApiException ex)
        {
            return ex.ToActionResult();
        }

        var familyPlans = products
            .Where(p => string.Equals(p.ProductFamily?.Handle, _maxioOptions.ProductFamilyHandle?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (familyPlans.Count > 0)
        {
            var defaultPlan = familyPlans.OrderByDescending(p => p.PriceInCents).First();
            foreach (var plan in familyPlans)
            {
                response.Plans.Add(SubscriptionMapper.ToPlanDto(plan, isDefault: ReferenceEquals(plan, defaultPlan)));
            }
        }

        return Ok(response);
    }
}
