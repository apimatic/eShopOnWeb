using System;
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

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly IRepository<Subscription> _subscriptionRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IMaxioApiClient maxioClient, IRepository<Subscription> subscriptionRepo,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _subscriptionRepo = subscriptionRepo;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Create subscription",
        Description = "Subscribe to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return BadRequest(new ErrorResponse { Message = "Http context not available" });

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new ErrorResponse { Message = "User not authenticated" });

        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown@example.com";
        var givenName = httpContext.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
        var surname = httpContext.User.FindFirst(ClaimTypes.Surname)?.Value ?? "Unknown";

        var customer = await _maxioClient.FindOrCreateCustomerAsync(userId, email, givenName, surname);
        if (customer == null)
            return BadRequest(new ErrorResponse { Message = "Failed to create or find Maxio customer" });

        var existing = (await _subscriptionRepo.ListAsync())
            .FirstOrDefault(s => s.UserId == userId && s.ProductHandle == request.ProductHandle);

        if (existing != null)
        {
            var existingSubscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id);
            var activeSubscription = existingSubscriptions
                .FirstOrDefault(s => s.Product.Handle == request.ProductHandle && s.State == "active");

            if (activeSubscription != null)
                return BadRequest(new ErrorResponse
                {
                    Message = "You already have an active subscription for this plan"
                });
        }

        var subscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, request.ProductHandle);
        if (subscription == null)
            return BadRequest(new ErrorResponse { Message = "Failed to create subscription" });

        var dbSubscription = new Subscription
        {
            UserId = userId,
            MaxioCustomerId = customer.Id,
            MaxioSubscriptionId = subscription.Id,
            ProductHandle = request.ProductHandle,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };

        await _subscriptionRepo.AddAsync(dbSubscription);

        var result = new CreateSubscriptionResponse
        {
            Id = subscription.Id,
            CustomerId = customer.Id,
            ProductHandle = request.ProductHandle,
            State = subscription.State,
            PriceMonthly = subscription.Product.PriceInCents / 100m,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt
        };

        return CreatedAtAction(nameof(GetMySubscriptionsEndpoint), new { }, result);
    }
}
