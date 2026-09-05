using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, ILogger<CreateSubscriptionEndpoint> logger) =>
            {
                if (string.IsNullOrEmpty(request.ProductHandle))
                {
                    var response = new ErrorResponse(request.CorrelationId(), "ProductHandle is required");
                    return Results.BadRequest(response);
                }

                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var email = user.FindFirst(ClaimTypes.Email)?.Value;

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
                {
                    return Results.Unauthorized();
                }

                var firstName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
                var lastName = user.FindFirst(ClaimTypes.Surname)?.Value ?? "Account";

                try
                {
                    logger.LogInformation("Creating subscription for user {UserId} with product {ProductHandle}", userId, request.ProductHandle);

                    var customer = await subscriptionService.GetOrCreateCustomerAsync(userId, email, firstName, lastName);

                    if (customer.Id == null)
                    {
                        var failResp = new ErrorResponse(request.CorrelationId(), "Failed to create customer");
                        return Results.BadRequest(failResp);
                    }

                    var subscription = await subscriptionService.CreateSubscriptionAsync(customer.Id.Value, request.ProductHandle);

                    var subResp = new CreateSubscriptionResponse(request.CorrelationId())
                    {
                        Subscription = new SubscriptionDto
                        {
                            Id = subscription.Id,
                            State = subscription.State,
                            ProductHandle = subscription.ProductHandle ?? string.Empty,
                            CustomerId = subscription.CustomerId ?? 0,
                            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                            NextAssessmentAt = subscription.NextAssessmentAt,
                            ActivatedAt = subscription.ActivatedAt ?? DateTime.UtcNow,
                            CreatedAt = subscription.CreatedAt
                        }
                    };

                    logger.LogInformation("Successfully created subscription {SubscriptionId}", subscription.Id);

                    return Results.Created($"/api/subscriptions/{subscription.Id}", subResp);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error creating subscription");
                    var errResp = new ErrorResponse(request.CorrelationId(), $"Failed to create subscription: {ex.Message}");
                    return Results.BadRequest(errResp);
                }
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        throw new NotImplementedException("This method should not be called directly");
    }
}
