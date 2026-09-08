using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a signed-in shopper can subscribe to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the subscription plans that are available to subscribe to on the configured Maxio Advanced Billing product family.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        var plans = await _subscriptionService.ListAvailablePlansAsync(cancellationToken);
        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                ProductId = (int)(plan.Id ?? 0),
                Handle = plan.Handle ?? string.Empty,
                Name = plan.Name ?? string.Empty,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Price = plan.PriceInCents / 100m,
                Interval = plan.Interval ?? 1,
                IntervalUnit = plan.IntervalUnit ?? "month",
                HasTrial = (plan.TrialInterval ?? 0) > 0,
                RequiresPaymentMethod = plan.RequireCreditCard
            });
        }

        return response;
    }
}
