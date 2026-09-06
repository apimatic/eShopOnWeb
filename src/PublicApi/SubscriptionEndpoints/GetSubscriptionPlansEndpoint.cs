using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get available subscription plans
/// </summary>
public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithRequest<GetSubscriptionPlansRequest>
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _maxioSettings;

    public GetSubscriptionPlansEndpoint(IMaxioClient maxioClient, MaxioSettings maxioSettings)
    {
        _maxioClient = maxioClient;
        _maxioSettings = maxioSettings;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Returns a list of available subscription plans from Maxio",
        OperationId = "subscriptions.getplans",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(
        GetSubscriptionPlansRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var products = await _maxioClient.GetProductsAsync(_maxioSettings.ProductFamilyHandle);
            response.Plans = products
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Price = p.PriceInCents / 100m,
                    Description = p.Description
                })
                .ToList();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return response;
    }
}
