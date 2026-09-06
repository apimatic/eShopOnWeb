using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IRepository<Subscription> _subscriptionRepo;
    private readonly IMaxioApiClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(IRepository<Subscription> subscriptionRepo, IMaxioApiClient maxioClient,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionRepo = subscriptionRepo;
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Get my subscriptions",
        Description = "Get current user's subscriptions",
        OperationId = "subscriptions.list_mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return BadRequest(new ErrorResponse { Message = "Http context not available" });

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new ErrorResponse { Message = "User not authenticated" });

        var userSubscriptions = (await _subscriptionRepo.ListAsync())
            .Where(s => s.UserId == userId)
            .ToList();

        var response = new GetMySubscriptionsResponse();

        foreach (var sub in userSubscriptions)
        {
            var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(sub.MaxioCustomerId);
            var maxioSub = subscriptions.FirstOrDefault(s => s.Id == sub.MaxioSubscriptionId);

            if (maxioSub != null)
            {
                response.Subscriptions.Add(new SubscriptionDetailDto
                {
                    Id = maxioSub.Id,
                    ProductHandle = maxioSub.Product.Handle,
                    ProductName = maxioSub.Product.Name,
                    State = maxioSub.State,
                    PriceMonthly = maxioSub.Product.PriceInCents / 100m,
                    CurrentPeriodEndsAt = maxioSub.CurrentPeriodEndsAt,
                    NextAssessmentAt = maxioSub.NextAssessmentAt,
                    ActivatedAt = maxioSub.ActivatedAt
                });
            }
        }

        return Ok(response);
    }
}
