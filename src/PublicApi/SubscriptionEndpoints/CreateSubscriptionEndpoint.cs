using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a billing customer exists for the user and enrolls them in the given plan. Idempotent: subscribing again returns the existing subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(Guid.NewGuid());

        UserSubscriptionView subscription;
        try
        {
            subscription = await _subscriptionService.SubscribeAsync(User.Identity!.Name!, request.PlanHandle, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CreateSubscriptionResponse(Guid.NewGuid())
            {
                Error = ex.Message
            });
        }

        response.Subscription = Map(subscription);
        return Ok(response);
    }

    private static SubscriptionDto Map(UserSubscriptionView subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            State = subscription.State,
            Reference = subscription.Reference,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Price = ListSubscriptionPlansEndpoint.FormatPrice(subscription.PriceInCents),
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            NextBillingDate = subscription.NextBillingDate,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            BillingCustomerId = subscription.MaxioCustomerId
        };
    }
}