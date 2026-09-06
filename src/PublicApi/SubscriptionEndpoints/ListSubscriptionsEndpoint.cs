using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List user's subscriptions
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionsResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListSubscriptionsEndpoint(IMaxioClient maxioClient, IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionsResponse();

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                response.Success = false;
                response.Message = "User not authenticated";
                return Unauthorized(response);
            }

            var customer = await _maxioClient.GetOrCreateCustomerAsync(userId, "", "", "");
            if (customer == null)
            {
                response.Success = true;
                response.Subscriptions = new List<UserSubscriptionDto>();
                response.Message = "No customer found";
                return Ok(response);
            }

            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id);

            response.Success = true;
            response.Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
            {
                SubscriptionId = s.Id,
                CustomerId = s.CustomerId,
                ProductHandle = s.ProductHandle,
                State = s.State,
                BalanceInCents = s.BalanceInCents,
                Balance = s.BalanceInCents / 100m,
                NextBillingAt = s.NextBillingAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Failed to retrieve subscriptions: {ex.Message}";
            return BadRequest(response);
        }
    }
}

public class ListSubscriptionsResponse : BaseResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string? ProductHandle { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public decimal Balance { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
