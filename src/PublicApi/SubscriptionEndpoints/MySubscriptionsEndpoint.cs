using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<object>
    .WithActionResult<System.Collections.Generic.List<MySubscriptionResponse>>
{
    private readonly MaxioSubscriptionService _service;
    public MySubscriptionsEndpoint(MaxioSubscriptionService service) => _service = service;

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "My subscriptions", OperationId = "subscription.my", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<System.Collections.Generic.List<MySubscriptionResponse>>> HandleAsync(object request, CancellationToken cancellationToken = default)
    {
        var userRef = User.Identity?.Name ?? "anonymous";
        var customer = await _service.GetOrCreateCustomerAsync(userRef + "@example.com", userRef, cancellationToken);
        if (customer?.Customer == null)
            return Ok(new System.Collections.Generic.List<MySubscriptionResponse>());

        var subs = await _service.ListSubscriptionsAsync(customer.Customer.Id ?? 0, null, cancellationToken);
        var result = subs.Select(s => new MySubscriptionResponse
        {
            Id = s.Subscription?.Id ?? 0,
            PlanHandle = s.Subscription?.Product?.Handle ?? "",
            State = s.Subscription?.State ?? "",
            StartedAt = s.Subscription?.ActivatedAt?.ToString("yyyy-MM-dd") ?? "",
            NextBillingDate = s.Subscription?.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? "",
            Amount = (s.Subscription?.CurrentBillingAmountInCents ?? 0) / 100m
        }).ToList();
        return Ok(result);
    }
}
