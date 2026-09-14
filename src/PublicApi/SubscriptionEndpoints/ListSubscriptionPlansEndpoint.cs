using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for sign-up (the configured Maxio product family).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the recurring subscription plans the signed-in user can subscribe to",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);

        var response = new ListSubscriptionPlansResponse();
        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = plan.Id,
                Handle = plan.Handle,
                Name = plan.Name,
                Price = plan.Price,
                Currency = plan.Currency,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit
            });
        }

        return response;
    }
}
