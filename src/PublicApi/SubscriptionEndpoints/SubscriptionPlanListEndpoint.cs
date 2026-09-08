using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the plans a signed-in shopper can subscribe to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlanListEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<SubscriptionPlanListResponse>
{
    private readonly ISubscriptionBillingService _subscriptionService;
    private readonly ILogger<SubscriptionPlanListEndpoint> _logger;

    public SubscriptionPlanListEndpoint(
        ISubscriptionBillingService subscriptionService,
        ILogger<SubscriptionPlanListEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the subscription plans of the configured Maxio product family",
        OperationId = "subscriptions.plans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionPlanListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _subscriptionService.ListPlansAsync(cancellationToken);
            var response = new SubscriptionPlanListResponse();
            response.Plans.AddRange(plans.Select(SubscriptionDtoMapping.ToPlanDto));
            return response;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio call failed while listing subscription plans.");
            return SubscriptionEndpointHelpers.Error(502, "The billing provider could not list subscription plans.");
        }
        catch (MaxioConfigurationException ex)
        {
            _logger.LogError(ex, "Maxio is not configured.");
            return SubscriptionEndpointHelpers.Error(503, "The billing provider is not configured.");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogError(ex, "Transport error while listing subscription plans.");
            return SubscriptionEndpointHelpers.Error(502, "The billing provider could not be reached.");
        }
    }
}
