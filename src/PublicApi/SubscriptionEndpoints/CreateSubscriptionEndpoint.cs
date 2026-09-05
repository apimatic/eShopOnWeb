using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IMaxioService maxioService,
        AppIdentityDbContext identityDbContext,
        UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _identityDbContext = identityDbContext;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Creates a new subscription for the current user",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" }
    )]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return BadRequest("Plan handle is required");
            }

            // Get current user from claims
            var userName = User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized("User not found in token");
            }

            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Unauthorized("User not found");
            }

            // Get or create Maxio customer for this user
            var existingMapping = _identityDbContext.MaxioSubscriptionMappings?
                .FirstOrDefault(m => m.ApplicationUserId == user.Id);

            int maxioCustomerId;
            if (existingMapping != null)
            {
                maxioCustomerId = existingMapping.MaxioCustomerId;
            }
            else
            {
                // Create new Maxio customer
                var customer = await _maxioService.GetOrCreateCustomer(
                    user.Email ?? "",
                    user.UserName ?? "User",
                    "",
                    user.Id);

                if (customer == null)
                {
                    return StatusCode(500, "Failed to create Maxio customer");
                }

                maxioCustomerId = customer.Id;

                // Store the mapping
                var mapping = new MaxioSubscriptionMapping
                {
                    ApplicationUserId = user.Id,
                    MaxioCustomerId = maxioCustomerId
                };
                _identityDbContext.MaxioSubscriptionMappings?.Add(mapping);
                await _identityDbContext.SaveChangesAsync();
            }

            // Create subscription in Maxio
            var subscription = await _maxioService.CreateSubscription(maxioCustomerId, request.PlanHandle);

            if (subscription == null)
            {
                return StatusCode(500, "Failed to create subscription");
            }

            return Ok(new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
                Message = "Subscription created successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string? Message { get; set; }
}
