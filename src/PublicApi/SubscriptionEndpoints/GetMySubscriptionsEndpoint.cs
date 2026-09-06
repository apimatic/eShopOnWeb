using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IMaxioApiService _maxioApi;

    public GetMySubscriptionsEndpoint(IMaxioApiService maxioApi)
    {
        _maxioApi = maxioApi;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get current user's subscriptions",
        Description = "Returns list of subscriptions for the authenticated user",
        OperationId = "subscriptions.getMySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(GetMySubscriptionsResponse), 200)]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetMySubscriptionsResponse(Guid.NewGuid());
        response.Subscriptions = new List<MySubscriptionDto>();

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            response.Success = false;
            response.ErrorMessage = "User not authenticated";
            return Unauthorized();
        }

        var customer = await _maxioApi.LookupCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            response.Success = true;
            response.Message = "No subscriptions found";
            return response;
        }

        var subscriptions = await _maxioApi.ListCustomerSubscriptionsAsync(customer.Id);
        if (subscriptions?.Subscriptions != null)
        {
            foreach (var sub in subscriptions.Subscriptions)
            {
                response.Subscriptions.Add(new MySubscriptionDto
                {
                    SubscriptionId = sub.Id,
                    State = sub.State,
                    ProductHandle = sub.ProductHandle,
                    NextBillingAt = sub.NextBillingAt,
                    MrrPerMonth = sub.MrrInCents.HasValue ? sub.MrrInCents.Value / 100m : 0,
                    CreatedAt = sub.CreatedAt,
                    UpdatedAt = sub.UpdatedAt
                });
            }
        }

        response.Success = true;
        response.Message = response.Subscriptions.Count > 0
            ? $"Found {response.Subscriptions.Count} subscription(s)"
            : "No active subscriptions";

        return response;
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal MrrPerMonth { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
