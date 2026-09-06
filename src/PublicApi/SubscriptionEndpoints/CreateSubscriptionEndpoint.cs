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
    private readonly IMaxioApiService _maxioApi;

    public CreateSubscriptionEndpoint(IMaxioApiService maxioApi)
    {
        _maxioApi = maxioApi;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribe a user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), 200)]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), 400)]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            response.Success = false;
            response.ErrorMessage = "Plan handle is required";
            return BadRequest(response);
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            response.Success = false;
            response.ErrorMessage = "User not authenticated";
            return Unauthorized(response);
        }

        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? $"{userId}@eshop.local";
        var firstName = User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
        var lastName = User.FindFirst(ClaimTypes.Surname)?.Value ?? userId;

        var plan = await _maxioApi.GetProductByHandleAsync(request.PlanHandle);
        if (plan == null)
        {
            response.Success = false;
            response.ErrorMessage = "Plan not found";
            return BadRequest(response);
        }

        var customer = await _maxioApi.GetOrCreateCustomerAsync(userId, firstName, lastName, email);
        if (customer == null)
        {
            response.Success = false;
            response.ErrorMessage = "Failed to create or retrieve customer";
            return BadRequest(response);
        }

        var subscription = await _maxioApi.CreateSubscriptionAsync(customer.Id, plan.Id, plan.Handle ?? "");
        if (subscription == null)
        {
            response.Success = false;
            response.ErrorMessage = "Failed to create subscription";
            return BadRequest(response);
        }

        response.Success = true;
        response.SubscriptionId = subscription.Id;
        response.State = subscription.State;
        response.CustomerMaxioId = customer.Id;
        response.PlanName = plan.Name;
        response.PricePerMonth = plan.PriceInCents / 100m;
        response.NextBillingAt = subscription.NextBillingAt;
        response.Message = $"Successfully subscribed to {plan.Name}";

        return response;
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public int CustomerMaxioId { get; set; }
    public string? PlanName { get; set; }
    public decimal PricePerMonth { get; set; }
    public DateTime? NextBillingAt { get; set; }
}
