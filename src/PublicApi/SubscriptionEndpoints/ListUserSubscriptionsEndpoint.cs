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

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly Services.MaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(
        Services.MaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, Services.MaxioSubscriptionService service, UserManager<ApplicationUser> userManager, ILogger<ListUserSubscriptionsEndpoint> logger, CancellationToken ct) =>
            {
                return await HandleAsync(new EmptyRequest(), httpContext, service, userManager, logger, ct);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithName("ListUserSubscriptions")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, HttpContext httpContext, Services.MaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, ILogger<ListUserSubscriptionsEndpoint> logger, CancellationToken ct = default)
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
            var subscriptions = await subscriptionService.ListUserSubscriptionsAsync(customerId, ct);

            var response = new ListUserSubscriptionsResponse(request.CorrelationId());
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to list subscriptions");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error listing subscriptions");
            return Results.StatusCode(500);
        }
    }

    Task<IResult> IEndpoint<IResult, EmptyRequest>.HandleAsync(EmptyRequest request) =>
        throw new NotImplementedException();
}
