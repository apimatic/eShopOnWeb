using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, ListUserSubscriptionsRequest>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(
        MaxioSubscriptionService subscriptionService,
        ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsyncInternal(new ListUserSubscriptionsRequest(), httpContext, ct);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(ListUserSubscriptionsRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsyncInternal(
        ListUserSubscriptionsRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var response = new ListUserSubscriptionsResponse(request.CorrelationId());

        try
        {
            // Extract user identity from JWT claims
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                ?? httpContext.User.FindFirst("email")?.Value;

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userEmail) || string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Missing user identity in JWT claims");
                response.ErrorMessage = "User identity not found in token";
                return Results.Unauthorized();
            }

            _logger.LogInformation("Retrieving subscriptions for user {UserId} ({Email})", userId, userEmail);

            // Get or create customer to ensure we have the customer ID
            var customerId = await _subscriptionService.GetOrCreateCustomer(userEmail, userId, ct);

            // Retrieve subscriptions for the customer
            var subscriptions = await _subscriptionService.GetCustomerSubscriptions(customerId, ct);

            response.Subscriptions = subscriptions;
            response.Success = true;

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for user {UserId}",
                subscriptions.Count, userId);

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation retrieving subscriptions");
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving subscriptions");
            response.ErrorMessage = "An unexpected error occurred while retrieving subscriptions";
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
