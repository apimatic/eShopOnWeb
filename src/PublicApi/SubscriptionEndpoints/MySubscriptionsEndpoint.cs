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
/// Lists the subscriptions of the signed-in shopper.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _subscriptionService;
    private readonly ILogger<MySubscriptionsEndpoint> _logger;

    public MySubscriptionsEndpoint(
        ISubscriptionBillingService subscriptionService,
        ILogger<MySubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the subscriptions the signed-in shopper has with Maxio Advanced Billing",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        string? userEmail = SubscriptionEndpointHelpers.GetUserEmail(User);
        if (userEmail is null)
        {
            return SubscriptionEndpointHelpers.Error(401, "The token does not carry a user identity.");
        }

        try
        {
            var subscriptions = await _subscriptionService.ListSubscriptionsAsync(userEmail, cancellationToken);
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(SubscriptionDtoMapping.ToSubscriptionDtos(subscriptions));
            return response;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio call failed while listing subscriptions for user {UserEmail}.", userEmail);
            return SubscriptionEndpointHelpers.Error(502, "The billing provider could not list subscriptions.");
        }
        catch (MaxioConfigurationException ex)
        {
            _logger.LogError(ex, "Maxio is not configured.");
            return SubscriptionEndpointHelpers.Error(503, "The billing provider is not configured.");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogError(ex, "Transport error while listing subscriptions for user {UserEmail}.", userEmail);
            return SubscriptionEndpointHelpers.Error(502, "The billing provider could not be reached.");
        }
    }
}
