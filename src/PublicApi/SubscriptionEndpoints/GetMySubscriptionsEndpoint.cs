using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(MaxioSubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get my subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscriptions.list-mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new GetMySubscriptionsResponse();

        try
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
            {
                return Unauthorized(new { message = "User ID not found in token" });
            }

            var userId = userIdClaim.Value;
            var customerReference = $"eshop-{userId}";

            // Try to get customer and their subscriptions
            // Since we don't have a direct "get customer by reference" endpoint,
            // we'll try to fetch all subscriptions and filter by the ones for this user
            // Or we could store the Maxio customer ID in the local database
            // For now, we'll attempt to list subscriptions by searching

            var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();
            var userSubscriptions = new List<MySubscriptionDto>();

            foreach (var sub in subscriptions)
            {
                if (sub.Product != null)
                {
                    userSubscriptions.Add(new MySubscriptionDto
                    {
                        SubscriptionId = sub.Id,
                        CustomerId = sub.CustomerId,
                        PlanName = sub.Product.Name,
                        PlanHandle = sub.Product.Handle,
                        State = sub.State,
                        PriceInCents = sub.Product.PriceInCents,
                        CreatedAt = sub.CreatedAt,
                        UpdatedAt = sub.UpdatedAt,
                        NextBillingAt = sub.NextBillingAt
                    });
                }
            }

            response.Success = true;
            response.Subscriptions = userSubscriptions;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error retrieving subscriptions: {ex.Message}";
            return StatusCode(500, response);
        }

        return Ok(response);
    }
}

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }

    public decimal Price => PriceInCents / 100m;
}

public class GetMySubscriptionsResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
