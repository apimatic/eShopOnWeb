using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioClient _maxioClient;

    public MySubscriptionsEndpoint(IMaxioClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user from Maxio",
        OperationId = "subscription.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse();

        var userId = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var customerRef = $"eshop-{userId}";
        var customer = await _maxioClient.LookupCustomerByReferenceAsync(customerRef, cancellationToken);

        if (customer == null)
            return Ok(response);

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        response.Subscriptions = subscriptions.Select(s => new MySubscriptionDto
        {
            SubscriptionId = s.Id,
            State = s.State,
            PlanName = s.Product?.Name ?? "Unknown",
            PlanHandle = s.Product?.Handle,
            PlanPrice = s.Product?.Price ?? 0m,
            CurrentPeriodStartsAt = s.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingDate = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt
        }).ToList();

        return Ok(response);
    }
}
