using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in user to a plan. Idempotent: a repeated request for the same plan returns
/// the existing active subscription instead of creating a duplicate.
/// </summary>
[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer exists for the current user, then subscribes them to the requested plan. Idempotent.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest("A planHandle is required.");
        }

        string userName = User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Unauthorized();
        }

        var products = await _maxioClient.GetProductsAsync(cancellationToken);
        var product = products.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (product == null)
        {
            return BadRequest($"Unknown plan handle '{request.PlanHandle}'.");
        }

        // Ensure a Maxio customer exists for this user (idempotent: reference is unique in Maxio).
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer == null)
        {
            customer = await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = userName,
                LastName = "User",
                Email = user.Email ?? userName,
                Reference = userName
            }, cancellationToken);
        }

        // Idempotency: never create a second subscription for a user that already has an active one.
        var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var active = subscriptions.FirstOrDefault(s => IsActive(s.State));
        if (active != null)
        {
            if (string.Equals(active.Product?.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase))
            {
                response.Created = false;
                response.Subscription = SubscriptionMapper.ToSubscriptionDto(active);
                return Ok(response);
            }

            return Conflict($"User is already subscribed to plan '{active.Product?.Handle}'.");
        }

        var subscription = await _maxioClient.CreateSubscriptionAsync(request.PlanHandle, userName, cancellationToken);
        response.Created = true;
        response.Subscription = SubscriptionMapper.ToSubscriptionDto(subscription);

        return Ok(response);
    }

    private static bool IsActive(string? state)
    {
        return state is "active" or "trialing";
    }
}
