using System;
using System.Net.Http;
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
/// Subscribes the signed-in shopper to a plan. Idempotent: a shopper who already has a subscription to the
/// requested plan gets that subscription back rather than a second one.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint : EndpointBaseAsync.WithRequest<SubscribeRequest>.WithActionResult<SubscribeResponse>
{
    private readonly ISubscriptionBillingService _subscriptionService;
    private readonly ILogger<SubscribeEndpoint> _logger;

    public SubscribeEndpoint(
        ISubscriptionBillingService subscriptionService,
        ILogger<SubscribeEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Creates (or returns the existing) Maxio subscription for the signed-in shopper and the requested plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(
        SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return SubscriptionEndpointHelpers.Error(400, "planHandle is required.");
        }

        string? userEmail = SubscriptionEndpointHelpers.GetUserEmail(User);
        if (userEmail is null)
        {
            return SubscriptionEndpointHelpers.Error(401, "The token does not carry a user identity.");
        }

        try
        {
            SubscribeResult result = await _subscriptionService.EnsureSubscriptionAsync(
                userEmail, request.PlanHandle.Trim(), cancellationToken);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = SubscriptionDtoMapping.ToSubscriptionDto(result.Subscription),
                Created = result.Created
            };

            return result.Created
                ? Created("api/subscriptions", response)
                : Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            _logger.LogWarning("Subscribe failed: {Message}", ex.Message);
            return SubscriptionEndpointHelpers.Error(400, ex.Message);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio rejected subscription request for user {UserEmail} and plan {PlanHandle}.",
                userEmail, request.PlanHandle);
            // 4xx errors from Maxio (e.g. a plan that requires a stored payment method) are surfaced as-is;
            // upstream 5xx/transport problems become 502.
            int statusCode = ex.IsClientError ? 422 : 502;
            return SubscriptionEndpointHelpers.Error(statusCode,
                ex.IsClientError ? ex.Message : "The billing provider could not create the subscription.");
        }
        catch (MaxioConfigurationException ex)
        {
            _logger.LogError(ex, "Maxio is not configured.");
            return SubscriptionEndpointHelpers.Error(503, "The billing provider is not configured.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Transport error while creating a subscription for user {UserEmail}.", userEmail);
            return SubscriptionEndpointHelpers.Error(502, "The billing provider could not be reached.");
        }
    }
}
