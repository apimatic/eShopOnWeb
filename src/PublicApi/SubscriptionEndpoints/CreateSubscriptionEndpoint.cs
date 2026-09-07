using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequestBody
{
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequestBody>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioApiService _maxioApiService;
    private readonly MaxioSettings _maxioSettings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioApiService maxioApiService,
        MaxioSettings maxioSettings,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioApiService = maxioApiService;
        _maxioSettings = maxioSettings;
        _userManager = userManager;
        _identityContext = identityContext;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribe a user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequestBody request,
        CancellationToken cancellationToken = default)
    {
        // Get current user
        var userName = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { error = "User not authenticated" });
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Unauthorized(new { error = "User not found" });
        }

        if (string.IsNullOrEmpty(request.PlanHandle))
        {
            return BadRequest(new { error = "Plan handle is required" });
        }

        try
        {
            // Get or create Maxio customer
            var maxioCustomer = await _maxioApiService.CreateOrGetCustomerAsync(
                user.Email ?? "",
                user.UserName ?? "",
                "",
                $"eshop-{user.Id}");

            if (maxioCustomer == null)
            {
                return BadRequest(new { error = "Failed to create or get Maxio customer" });
            }

            // Store mapping
            var existingMapping = await _identityContext.MaxioCustomerMappings.FindAsync(user.Id, cancellationToken);
            if (existingMapping == null)
            {
                var mapping = new MaxioCustomerMapping
                {
                    ApplicationUserId = user.Id,
                    MaxioCustomerId = maxioCustomer.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _identityContext.MaxioCustomerMappings.Add(mapping);
            }
            else
            {
                existingMapping.MaxioCustomerId = maxioCustomer.Id;
                existingMapping.UpdatedAt = DateTime.UtcNow;
            }
            await _identityContext.SaveChangesAsync(cancellationToken);

            // Create subscription
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                customer_id = maxioCustomer.Id,
                product_handle = request.PlanHandle,
                payment_collection_method = "remittance",
                skip_billing_manifest_taxes = true
            };

            var subscription = await _maxioApiService.CreateSubscriptionAsync(subscriptionRequest);
            if (subscription == null)
            {
                return BadRequest(new { error = "Failed to create subscription" });
            }

            return new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductName = subscription.Product?.Name ?? "",
                PriceInCents = subscription.ProductPriceInCents,
                Price = subscription.ProductPriceInCents / 100m,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                Message = "Subscription created successfully"
            };
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionResponse
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string Message { get; set; } = "";
}
