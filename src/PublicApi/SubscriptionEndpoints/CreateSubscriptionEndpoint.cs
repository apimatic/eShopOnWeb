using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IMaxioService _maxioService;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        IRepository<Subscription> subscriptionRepository,
        IMaxioService maxioService)
    {
        _userManager = userManager;
        _subscriptionRepository = subscriptionRepository;
        _maxioService = maxioService;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Create a subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = User;
        var response = new CreateSubscriptionResponse();

        var appUser = await _userManager.GetUserAsync(user);
        if (appUser == null)
        {
            return Unauthorized();
        }

        try
        {
            var (customerId, subscriptionId) = await _maxioService.CreateOrGetSubscriptionAsync(
                appUser.Id,
                appUser.FirstName ?? appUser.UserName ?? "Unknown",
                appUser.LastName ?? "",
                appUser.Email ?? "",
                request.ProductHandle);

            // Store the subscription mapping in the database
            var existingSubscription = (await _subscriptionRepository.ListAsync()).FirstOrDefault(s =>
                s.UserId == appUser.Id && s.MaxioSubscriptionId == subscriptionId);

            if (existingSubscription == null)
            {
                var subscription = new Subscription
                {
                    UserId = appUser.Id,
                    MaxioCustomerId = customerId,
                    MaxioSubscriptionId = subscriptionId,
                    ProductHandle = request.ProductHandle,
                    PlanName = request.ProductHandle,
                    PriceInDollars = 0,
                    BillingState = "active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _subscriptionRepository.AddAsync(subscription);
            }

            response.SubscriptionId = subscriptionId;
            response.CustomerId = customerId;
            response.ProductHandle = request.ProductHandle;
            response.Status = "active";

            return Created($"api/subscriptions/{subscriptionId}", response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = null!;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public long SubscriptionId { get; set; }
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string Status { get; set; } = null!;
}
