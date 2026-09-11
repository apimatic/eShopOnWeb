using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using MaxioAdvancedBilling.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly MaxioSubscriptionService _service;
    public SubscribeEndpoint(MaxioSubscriptionService service) => _service = service;

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", OperationId = "subscription.create", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var userRef = User.Identity?.Name ?? "anonymous";
        var customer = await _service.GetOrCreateCustomerAsync(userRef + "@example.com", userRef, cancellationToken);
        if (customer?.Customer == null)
            return BadRequest(new SubscribeResponse { Success = false, Message = "Customer lookup/creation failed." });

        var customerId = customer.Customer.Id ?? 0;

        // Idempotence: check existing subscription for this customer + product
        var existing = await _service.ListSubscriptionsAsync(customerId, request.PlanId, cancellationToken);
        var active = existing.FirstOrDefault(s => s.Subscription?.Product?.Id == request.PlanId);
        if (active != null)
        {
            return Ok(new SubscribeResponse
            {
                Success = true,
                SubscriptionId = active.Subscription?.Id ?? 0,
                PlanHandle = active.Subscription?.Product?.Handle ?? "",
                State = active.Subscription?.State ?? "",
                NextBillingDate = active.Subscription?.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? "",
                Message = "Subscription already exists."
            });
        }

        var subReq = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductId = request.PlanId,
                CustomerReference = userRef,

                Reference = null,
                ExpiresAt = null,
                DeferSignup = false
            }
        };

        try
        {
            var created = await _service.CreateSubscriptionAsync(subReq, cancellationToken);
            return Ok(new SubscribeResponse
            {
                Success = true,
                SubscriptionId = created.Subscription?.Id ?? 0,
                PlanHandle = created.Subscription?.Product?.Handle ?? "",
                State = created.Subscription?.State ?? "",
                NextBillingDate = created.Subscription?.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? "",
                Message = "Subscribed successfully."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new SubscribeResponse { Success = false, Message = ex.Message });
        }
    }
}
