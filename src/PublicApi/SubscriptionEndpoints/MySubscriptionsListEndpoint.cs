using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class MySubscriptionsListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsListResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly ISubscriptionCustomerService _subscriptionCustomerService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(
        IMaxioService maxioService,
        ISubscriptionCustomerService subscriptionCustomerService,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioService = maxioService;
        _subscriptionCustomerService = subscriptionCustomerService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get current user's subscriptions",
        Description = "Retrieves all subscriptions for the currently authenticated user",
        OperationId = "subscriptions.list-my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsListResponse();

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                response.Error = "User not authenticated";
                return Unauthorized(response);
            }

            var mapping = await _subscriptionCustomerService.GetByUserIdAsync(userId);
            if (mapping == null)
            {
                response.Subscriptions = new();
                return Ok(response);
            }

            var subscriptions = await _maxioService.GetCustomerSubscriptionsAsync(mapping.MaxioCustomerId);

            response.Subscriptions = subscriptions.ConvertAll(s => new MaxioSubscriptionDto
            {
                Id = s.Id,
                ProductId = s.ProductId,
                CustomerId = s.CustomerId,
                State = s.State,
                CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
            });

            return Ok(response);
        }
        catch (Exception ex)
        {
            response.Error = $"An error occurred while retrieving subscriptions: {ex.Message}";
            return StatusCode(500, response);
        }
    }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
}

public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse() { }
    public MySubscriptionsListResponse(Guid correlationId) : base(correlationId) { }

    public List<MaxioSubscriptionDto> Subscriptions { get; set; } = new();
    public string? Error { get; set; }
}
