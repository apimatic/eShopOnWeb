using System;
using System.Collections.Generic;
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
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(
        IMaxioService maxioService,
        AppIdentityDbContext identityDbContext,
        UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _identityDbContext = identityDbContext;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get current user's subscriptions",
        Description = "Returns a list of subscriptions for the authenticated user",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "Subscriptions" }
    )]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
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

            // Get Maxio customer mapping for this user
            var mapping = _identityDbContext.MaxioSubscriptionMappings?
                .FirstOrDefault(m => m.ApplicationUserId == user.Id);

            if (mapping == null)
            {
                // No subscriptions yet
                return Ok(new GetMySubscriptionsResponse { Subscriptions = new List<SubscriptionDto>() });
            }

            // Get subscriptions from Maxio
            var maxioSubscriptions = await _maxioService.GetSubscriptionsByCustomerId(mapping.MaxioCustomerId);

            var subscriptions = maxioSubscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductName = s.Product?.Name,
                Price = s.Product != null ? s.Product.PriceInCents / 100m : null,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt
            }).ToList();

            return Ok(new GetMySubscriptionsResponse { Subscriptions = subscriptions });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsResponse
{
    public List<SubscriptionDto>? Subscriptions { get; set; }
}
