using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        ISubscriptionService subscriptionService,
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        try
        {
            // Get the username from JWT claims
            var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("No username found in JWT claims");
                return Results.Unauthorized();
            }

            // Get the user to retrieve the ID
            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
            {
                _logger.LogWarning("User not found: {Username}", username);
                return Results.Unauthorized();
            }

            var result = await _subscriptionService.CreateSubscriptionAsync(
                user.Id,
                request.FirstName,
                request.LastName,
                request.Email,
                request.ProductHandle);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Failed to create subscription: {Error}", result.Errors.FirstOrDefault());
                return Results.BadRequest(new { error = result.Errors.FirstOrDefault() });
            }

            var response = new CreateSubscriptionResponse
            {
                Subscription = new SubscriptionResponseDto
                {
                    Id = result.Value.Id ?? 0,
                    MaxioSubscriptionId = result.Value.MaxioSubscriptionId,
                    ProductHandle = result.Value.ProductHandle,
                    ProductName = result.Value.ProductName,
                    State = result.Value.State,
                    PriceInCents = result.Value.PriceInCents,
                    NextBillingAt = result.Value.NextBillingAt,
                    CreatedAt = result.Value.CreatedAt
                }
            };

            _logger.LogInformation("Created subscription for user {UserId}: {SubscriptionId}", user.Id, result.Value.MaxioSubscriptionId);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription");
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionResponseDto Subscription { get; set; } = new();
}

public class SubscriptionResponseDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
