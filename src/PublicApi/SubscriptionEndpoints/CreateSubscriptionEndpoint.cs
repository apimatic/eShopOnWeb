using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioApiService _maxioApiService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        IMaxioApiService maxioApiService,
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioApiService = maxioApiService;
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal principal) =>
                await Handle(request, principal))
           .Produces<CreateSubscriptionResponse>()
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status401Unauthorized)
           .WithTags("SubscriptionEndpoints")
           .WithName("CreateSubscription")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        return Results.BadRequest();
    }

    public async Task<IResult> Handle(CreateSubscriptionRequest request, ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var customerRef = user.Id;
            var customerEmail = user.Email ?? "";
            var nameParts = (user.UserName ?? "").Split('@', StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 0 ? nameParts[0] : "Customer";
            var lastName = nameParts.Length > 1 ? nameParts[1] : "User";

            var maxioCustomer = await _maxioApiService.GetOrCreateCustomerAsync(
                customerRef,
                customerEmail,
                firstName,
                lastName);

            if (maxioCustomer == null)
            {
                _logger.LogError($"Failed to create/get Maxio customer for user {userId}");
                return Results.BadRequest(new { error = "Failed to create customer record" });
            }

            var subscription = await _maxioApiService.CreateSubscriptionAsync(
                maxioCustomer.Id,
                request.ProductHandle);

            if (subscription == null)
            {
                _logger.LogError($"Failed to create subscription for customer {maxioCustomer.Id}");
                return Results.BadRequest(new { error = "Failed to create subscription" });
            }

            await _subscriptionService.CreateOrUpdateSubscriptionAsync(
                userId,
                maxioCustomer.Id,
                subscription.Id,
                request.ProductHandle,
                subscription);

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                CustomerId = maxioCustomer.Id,
                State = subscription.State,
                ProductHandle = subscription.Product?.Handle ?? request.ProductHandle,
                ProductName = subscription.Product?.Name ?? "",
                PriceInCents = subscription.ProductPriceInCents,
                IntervalUnit = subscription.Product?.IntervalUnit ?? "month",
                Interval = subscription.Product?.Interval ?? 1,
                ActivatedAt = subscription.ActivatedAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating subscription for user {userId}");
            return Results.BadRequest(new { error = "An error occurred while creating the subscription" });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = null!;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public long SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = null!;
    public int Interval { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
}
