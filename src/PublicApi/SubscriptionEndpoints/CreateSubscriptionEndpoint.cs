using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly CatalogContext _context;
    private readonly IRepository<UserSubscription> _subscriptionRepository;
    private readonly IReadRepository<SubscriptionPlan> _planRepository;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        IMaxioService maxioService,
        CatalogContext context,
        IRepository<UserSubscription> subscriptionRepository,
        IReadRepository<SubscriptionPlan> planRepository,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioService = maxioService;
        _context = context;
        _subscriptionRepository = subscriptionRepository;
        _planRepository = planRepository;
        _logger = logger;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
            var nameParts = (userName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 0 ? nameParts[0] : "User";
            var lastName = nameParts.Length > 1 ? nameParts[1] : userId;

            // Get or create customer in Maxio
            var customer = await _maxioService.GetOrCreateCustomerAsync(
                userId,
                userEmail,
                firstName,
                lastName,
                cancellationToken);

            if (customer == null)
            {
                _logger.LogError($"Failed to create/get Maxio customer for user {userId}");
                return StatusCode(500, new { error = "Failed to provision billing customer" });
            }

            // Get the product by handle
            var product = await _maxioService.GetProductByHandleAsync(request.PlanHandle, cancellationToken);
            if (product == null)
            {
                return BadRequest(new { error = "Invalid subscription plan" });
            }

            // Check for existing subscription for this plan
            var existingSubscriptions = await _maxioService.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken);
            var existingSubscription = existingSubscriptions.Where(s =>
                s.ProductId == product.Id && (s.State == "active" || s.State == "trialing"))
                .FirstOrDefault();

            if (existingSubscription != null)
            {
                return Ok(new CreateSubscriptionResponse
                {
                    MaxioSubscriptionId = existingSubscription.Id,
                    MaxioCustomerId = customer.Id,
                    PlanHandle = request.PlanHandle,
                    State = existingSubscription.State,
                    CurrentPeriodStartsAt = existingSubscription.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = existingSubscription.CurrentPeriodEndsAt,
                    Message = "Subscription already exists for this plan"
                });
            }

            // Create subscription
            var createRequest = new MaxioCreateSubscriptionRequest
            {
                CustomerId = customer.Id,
                ProductHandle = request.PlanHandle
            };

            var subscription = await _maxioService.CreateSubscriptionAsync(createRequest, cancellationToken);

            // Persist to local database for quick lookup
            var userSubscription = new UserSubscription
            {
                UserId = userId,
                SubscriptionPlanId = 0,
                MaxioSubscriptionId = subscription.Id,
                MaxioCustomerId = customer.Id,
                State = subscription.State,
                CurrentPeriodStartAt = subscription.CurrentPeriodStartsAt,
                CurrentPeriodEndAt = subscription.CurrentPeriodEndsAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _subscriptionRepository.AddAsync(userSubscription, cancellationToken);

            return Ok(new CreateSubscriptionResponse
            {
                MaxioSubscriptionId = subscription.Id,
                MaxioCustomerId = customer.Id,
                PlanHandle = request.PlanHandle,
                State = subscription.State,
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                Message = "Subscription created successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return StatusCode(500, new { error = "Failed to create subscription" });
        }
    }
}

public class CreateSubscriptionRequest
{
    public required string PlanHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public required string PlanHandle { get; set; }
    public required string State { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public string Message { get; set; } = "";
}
