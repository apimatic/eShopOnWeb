using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly ISubscriptionCustomerService _subscriptionCustomerService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioService maxioService,
        ISubscriptionCustomerService subscriptionCustomerService,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioService = maxioService;
        _subscriptionCustomerService = subscriptionCustomerService;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribes the current user to a subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                response.Error = "User not authenticated";
                return Unauthorized(response);
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                response.Error = "User not found";
                return NotFound(response);
            }

            var existingMapping = await _subscriptionCustomerService.GetByUserIdAsync(userId);
            int maxioCustomerId;

            if (existingMapping == null)
            {
                maxioCustomerId = await _maxioService.GetOrCreateMaxioCustomerAsync(userId, user.Email ?? userId);
                await _subscriptionCustomerService.AddAsync(userId, maxioCustomerId);
            }
            else
            {
                maxioCustomerId = existingMapping.MaxioCustomerId;
            }

            var subscription = await _maxioService.CreateSubscriptionAsync(maxioCustomerId, request.ProductHandle);

            response.Success = true;
            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt;
            response.CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt;
            response.NextBillingDate = subscription.NextAssessmentAt;

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            response.Error = ex.Message;
            return BadRequest(response);
        }
        catch (InvalidOperationException ex)
        {
            response.Error = ex.Message;
            return BadRequest(response);
        }
        catch (Exception ex)
        {
            response.Error = $"An error occurred while creating the subscription: {ex.Message}";
            return StatusCode(500, response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string? Error { get; set; }
}
