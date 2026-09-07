using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionInput>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(SubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Subscribe to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionInput request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse();

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { error = "User not authenticated" });
            }

            var user = _httpContextAccessor.HttpContext?.User;
            var email = user?.FindFirst(ClaimTypes.Email)?.Value ?? $"{userId}@eshop.local";
            var firstName = user?.FindFirst("given_name")?.Value ?? "User";
            var lastName = user?.FindFirst("family_name")?.Value ?? "";

            var customerId = await _subscriptionService.GetOrCreateCustomerAsync(
                userId, email, firstName, lastName, cancellationToken);

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                customerId, request.ProductHandle, userId, cancellationToken);

            response.Subscription = subscription;
            return Ok(response);
        }
        catch (SubscriptionException ex)
        {
            return StatusCode(ex.HttpStatusCode ?? 500, new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionInput
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }
}
