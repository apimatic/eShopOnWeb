using System;
using System.Collections.Generic;
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
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(SubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List user's subscriptions",
        Description = "Retrieve subscriptions for the logged-in user",
        OperationId = "subscriptions.listMy",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse();

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { error = "User not authenticated" });
            }

            try
            {
                var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(
                    await GetCustomerIdAsync(userId, cancellationToken),
                    cancellationToken);
                response.Subscriptions.AddRange(subscriptions);
                return Ok(response);
            }
            catch (SubscriptionException ex) when (ex.HttpStatusCode == 404)
            {
                return Ok(response);
            }
        }
        catch (SubscriptionException ex)
        {
            return StatusCode(ex.HttpStatusCode ?? 500, new { error = ex.Message });
        }
    }

    private async Task<int> GetCustomerIdAsync(string userId, CancellationToken ct)
    {
        var email = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value ?? $"{userId}@eshop.local";
        var firstName = _httpContextAccessor.HttpContext?.User.FindFirst("given_name")?.Value ?? "User";
        var lastName = _httpContextAccessor.HttpContext?.User.FindFirst("family_name")?.Value ?? "";

        return await _subscriptionService.GetOrCreateCustomerAsync(userId, email, firstName, lastName, ct);
    }
}

public class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}
