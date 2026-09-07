using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly Services.MaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        Services.MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, Services.MaxioSubscriptionService service, UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger, CancellationToken ct) =>
            {
                return await HandleAsync(request, httpContext, service, userManager, logger, ct);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, Services.MaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger, CancellationToken ct = default)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("No user ID found in JWT claims");
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                logger.LogWarning("User not found: {UserId}", userId);
                return Results.NotFound(new { error = "User not found" });
            }

            var customerId = await subscriptionService.EnsureCustomerExistsAsync(userId, user.Email ?? "", user.UserName ?? "", "", ct);
            var subscription = await subscriptionService.CreateSubscriptionAsync(customerId, request.ProductHandle, ct);

            var response = new CreateSubscriptionResponse(request.CorrelationId());
            response.Subscription = subscription;
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Subscription creation failed");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during subscription creation");
            return Results.StatusCode(500);
        }
    }

    Task<IResult> IEndpoint<IResult, CreateSubscriptionRequest>.HandleAsync(CreateSubscriptionRequest request) =>
        throw new NotImplementedException();
}
