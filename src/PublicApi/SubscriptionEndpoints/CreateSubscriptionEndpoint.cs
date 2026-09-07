using System;
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
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription for the current user",
        Description = "Subscribes the current user to a plan",
        OperationId = "subscription.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? throw new Exception("User ID not found in claims");

            var email = User.FindFirst(ClaimTypes.Email)?.Value
                ?? throw new Exception("Email not found in claims");

            var firstName = User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
            var lastName = User.FindFirst(ClaimTypes.Surname)?.Value ?? "Account";

            var maxioCustomerId = await _subscriptionService.EnsureCustomerExistsAsync(
                userId, email, firstName, lastName, cancellationToken);

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                userId, maxioCustomerId, request.ProductId, request.ProductHandle, cancellationToken);

            return new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                ProductHandle = subscription.ProductHandle,
                State = subscription.State,
                BalanceInCents = subscription.BalanceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = null!;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
