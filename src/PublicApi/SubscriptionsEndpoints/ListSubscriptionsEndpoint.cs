using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class ListSubscriptionsRequest : BaseRequest
{
}

public class ListSubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<ListSubscriptionsRequest>
    .WithActionResult<ListSubscriptionsResponse>
{
    private readonly IMaxioService _maxioService;

    public ListSubscriptionsEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List user subscriptions",
        Description = "Lists all subscriptions for the authenticated user",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionsEndpoints" })
    ]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public override async Task<ActionResult<ListSubscriptionsResponse>> HandleAsync(
        ListSubscriptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionsResponse(request.CorrelationId());

        try
        {
            var userName = User?.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized();
            }

            var email = User?.FindFirst(ClaimTypes.Email)?.Value ?? userName;

            var customer = await _maxioService.GetOrCreateCustomerAsync(userName, email);
            if (customer?.Id == null)
            {
                return BadRequest(new { error = "Failed to create or retrieve customer" });
            }

            var subscriptions = await _maxioService.ListCustomerSubscriptionsAsync((int)customer.Id);

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State?.ToString(),
                ProductName = s.Product?.Name,
                ProductHandle = s.Product?.Handle,
                CurrentBillingAmountInCents = s.CurrentBillingAmountInCents,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                CreatedAt = s.CreatedAt,
                ActivatedAt = s.ActivatedAt,
                UpdatedAt = s.UpdatedAt
            }));

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to retrieve subscriptions", details = ex.Message });
        }
    }
}
