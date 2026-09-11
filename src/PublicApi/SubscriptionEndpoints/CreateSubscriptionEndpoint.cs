using System.ComponentModel.DataAnnotations;
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

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithResult<ActionResult<CreateSubscriptionResponse>>
{
    private readonly MaxioService _maxio;

    public CreateSubscriptionEndpoint(MaxioService maxio) => _maxio = maxio;

    [Authorize]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync([FromBody] CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var userId = GetUserReference();
        var email = GetUserEmail();
        var firstName = request.FirstName ?? "User";
        var lastName = request.LastName ?? "User";

        // Idempotent customer (search by reference = user id)
        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            customer = await _maxio.CreateCustomerAsync(email ?? $"{userId}@example.com", firstName, lastName, userId, cancellationToken);
        }

        // Idempotent subscription: if already active for this product, return existing
        var existing = await _maxio.GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var active = existing.FirstOrDefault(s => s.State == "active" && s.ProductHandle == request.PlanHandle);
        if (active != null)
        {
            return new CreateSubscriptionResponse(active.Id, active.State, active.ProductHandle, active.CustomerId, active.NextAssessmentAt ?? "", active.BalanceInCents);
        }

        var sub = await _maxio.CreateSubscriptionAsync(customer.Id, request.PlanHandle, cancellationToken);
        return new CreateSubscriptionResponse(sub.Id, sub.State, sub.ProductHandle, sub.CustomerId, sub.NextAssessmentAt ?? "", sub.BalanceInCents);
    }

    private string GetUserReference()
    {
        // In JWT, identity claim; fall back to name identifier or email prefix
        return "user-" + (User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst(ClaimTypes.Email)?.Value ?? "anonymous");
    }

    private string? GetUserEmail()
    {
        return User?.FindFirst(ClaimTypes.Email)?.Value ?? User?.Identity?.Name;
    }
}

public class CreateSubscriptionRequest
{
    [Required] public string PlanHandle { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public record CreateSubscriptionResponse(int Id, string State, string PlanHandle, int CustomerId, string NextAssessmentAt, int BalanceInCents);
