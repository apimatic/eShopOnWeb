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
/// Lists the authenticated user's subscriptions.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the user's subscriptions as recorded in the billing system",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        var subscriptions = await _subscriptionService.ListUserSubscriptionsAsync(User.Identity!.Name!, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            State = s.State,
            Reference = s.Reference,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            Price = ListSubscriptionPlansEndpoint.FormatPrice(s.PriceInCents),
            Interval = s.Interval,
            IntervalUnit = s.IntervalUnit,
            NextBillingDate = s.NextBillingDate,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt,
            BillingCustomerId = s.MaxioCustomerId
        }));

        return Ok(response);
    }
}