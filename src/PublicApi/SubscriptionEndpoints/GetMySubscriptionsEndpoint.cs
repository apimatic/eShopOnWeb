using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IMaxioApiService _maxioApiService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(
        IMaxioApiService maxioApiService,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioApiService = maxioApiService;
        _userManager = userManager;
        _identityContext = identityContext;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get current user's subscriptions",
        Description = "Retrieve all subscriptions for the authenticated user",
        OperationId = "subscriptions.getMine",
        Tags = new[] { "Subscriptions" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
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

        // Get Maxio customer ID from mapping
        var mapping = await _identityContext.MaxioCustomerMappings
            .FirstOrDefaultAsync(m => m.ApplicationUserId == user.Id, cancellationToken);

        if (mapping == null)
        {
            // No subscriptions yet
            return new GetMySubscriptionsResponse
            {
                Subscriptions = new List<UserSubscriptionDto>(),
                Message = "No subscriptions found"
            };
        }

        // Get subscriptions from Maxio
        var maxioSubscriptions = await _maxioApiService.GetCustomerSubscriptionsAsync(mapping.MaxioCustomerId);
        var subscriptions = new List<UserSubscriptionDto>();

        foreach (var sub in maxioSubscriptions)
        {
            subscriptions.Add(new UserSubscriptionDto
            {
                SubscriptionId = sub.Id,
                State = sub.State,
                ProductName = sub.Product?.Name ?? "",
                ProductHandle = sub.Product?.Handle ?? "",
                PriceInCents = sub.ProductPriceInCents,
                Price = sub.ProductPriceInCents / 100m,
                BalanceInCents = sub.BalanceInCents,
                CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                NextAssessmentAt = sub.NextAssessmentAt,
                ActivatedAt = sub.ActivatedAt,
                CanceledAt = sub.CanceledAt
            });
        }

        return new GetMySubscriptionsResponse
        {
            Subscriptions = subscriptions,
            Message = subscriptions.Count == 0 ? "No active subscriptions" : $"Found {subscriptions.Count} subscription(s)"
        };
    }
}

public class GetMySubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    public string Message { get; set; } = "";
}

public class UserSubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public long BalanceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
}
