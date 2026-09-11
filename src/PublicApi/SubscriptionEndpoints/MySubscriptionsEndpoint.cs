using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithResult<ActionResult<List<MySubscriptionResponse>>>
{
    private readonly MaxioService _maxio;

    public MySubscriptionsEndpoint(MaxioService maxio) => _maxio = maxio;

    [Authorize]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Get my subscriptions", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<MySubscriptionResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetUserReference();
        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
            return new List<MySubscriptionResponse>();

        var subs = await _maxio.GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subs.Select(s => new MySubscriptionResponse(s.Id, s.State, s.ProductHandle, s.NextAssessmentAt ?? "", s.BalanceInCents)).ToList();
    }

    private string GetUserReference()
    {
        return "user-" + (User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst(ClaimTypes.Email)?.Value ?? "anonymous");
    }
}

public record MySubscriptionResponse(int Id, string State, string PlanHandle, string NextAssessmentAt, int BalanceInCents);
