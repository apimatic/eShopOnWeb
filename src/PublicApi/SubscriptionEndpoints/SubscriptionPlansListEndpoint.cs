using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans that are available to subscribe to.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly MaxioBillingOptions _options;
    private readonly ILogger<SubscriptionPlansListEndpoint> _logger;

    public SubscriptionPlansListEndpoint(IOptions<MaxioBillingOptions> options, ILogger<SubscriptionPlansListEndpoint> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<SubscriptionPlansListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var response = new SubscriptionPlansListResponse();

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            return SubscriptionEndpointErrors.Problem(
                StatusCodes.Status503ServiceUnavailable,
                "The Maxio product family is not configured. Set the Maxio:ProductFamilyHandle setting.");
        }

        try
        {
            var plans = await billingService.ListProductsInFamilyAsync(_options.ProductFamilyHandle!, CancellationToken.None);

            response.Plans.AddRange(plans
                .Where(product => product.ArchivedAt is null)
                .OrderBy(product => product.PriceInCents)
                .Select(SubscriptionPlanDto.FromMaxio));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Maxio subscription plans.");
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }
}
