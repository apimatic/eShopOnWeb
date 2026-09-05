using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.AspNetCore.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IMaxioService maxioService,
        IRepository<Subscription> subscriptionRepository,
        UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _subscriptionRepository = subscriptionRepository;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribes the authenticated user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userIdClaim = (HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? HttpContext.User.FindFirst("sub")?.Value);

            if (string.IsNullOrEmpty(userIdClaim))
            {
                response.Success = false;
                response.Error = "User not found in token";
                return Unauthorized(response);
            }

            var user = await _userManager.FindByIdAsync(userIdClaim);
            if (user == null)
            {
                response.Success = false;
                response.Error = "User not found";
                return NotFound(response);
            }

            var customerId = await _maxioService.GetOrCreateCustomerAsync(user.Id, user.Email ?? userIdClaim);
            if (!customerId.HasValue)
            {
                response.Success = false;
                response.Error = "Failed to create or retrieve customer";
                return BadRequest(response);
            }

            var subscription = await _maxioService.CreateSubscriptionAsync(
                customerId.Value,
                "eshop-subscribe",
                request.PlanHandle);

            if (subscription == null)
            {
                response.Success = false;
                response.Error = "Failed to create subscription";
                return BadRequest(response);
            }

            var dbSubscription = new Subscription
            {
                UserId = user.Id,
                MaxioCustomerId = customerId.Value,
                MaxioSubscriptionId = subscription.Id,
                PlanHandle = request.PlanHandle,
                State = subscription.State,
                CreatedAt = subscription.CreatedAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };

            await _subscriptionRepository.AddAsync(dbSubscription);

            response.Success = true;
            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.CreatedAt = subscription.CreatedAt;
            response.CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt;
            response.PlanHandle = subscription.PlanHandle;

            return Ok(response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            return BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
}
