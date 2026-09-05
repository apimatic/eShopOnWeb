using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, ISubscriptionService>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<GetMySubscriptionsEndpoint> _logger;

    public GetMySubscriptionsEndpoint(ISubscriptionService subscriptionService, ILogger<GetMySubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, ISubscriptionService subscriptionService, ILogger<GetMySubscriptionsEndpoint> logger) =>
            {
                var correlationId = Guid.NewGuid();
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    logger.LogInformation("Fetching subscriptions for user {UserId}", userId);

                    var email = user.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
                    var firstName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
                    var lastName = user.FindFirst(ClaimTypes.Surname)?.Value ?? "Account";

                    var customer = await subscriptionService.GetOrCreateCustomerAsync(userId, email, firstName, lastName);

                    if (customer.Id == null)
                    {
                        var failResp = new ErrorResponse(correlationId, "Failed to find customer");
                        return Results.BadRequest(failResp);
                    }

                    var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(customer.Id.Value);

                    var response = new GetSubscriptionsResponse(correlationId);
                    response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
                    {
                        Id = s.Id,
                        State = s.State,
                        ProductHandle = s.ProductHandle ?? string.Empty,
                        CustomerId = s.CustomerId ?? 0,
                        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                        NextAssessmentAt = s.NextAssessmentAt,
                        ActivatedAt = s.ActivatedAt ?? DateTime.UtcNow,
                        CreatedAt = s.CreatedAt
                    }).ToList();

                    logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for user {UserId}", subscriptions.Count, userId);

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error fetching subscriptions");
                    var errResp = new ErrorResponse(correlationId, $"Failed to fetch subscriptions: {ex.Message}");
                    return Results.BadRequest(errResp);
                }
            })
            .Produces<GetSubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        throw new NotImplementedException("This method should not be called directly");
    }
}

public class GetMySubscriptionsRequest : BaseRequest
{
}
