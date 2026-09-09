using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in the billing system of record.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly SubscriptionUserContext _userContext;
    private readonly ISubscriptionBillingService _billingService;

    public MySubscriptionsEndpoint(SubscriptionUserContext userContext, ISubscriptionBillingService billingService)
    {
        _userContext = userContext;
        _billingService = billingService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the authenticated user's subscriptions as recorded in the billing system of record.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await _userContext.GetApplicationUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        System.Collections.Generic.IReadOnlyList<ApplicationCore.Models.Billing.SubscriptionDetails> subscriptions;
        try
        {
            subscriptions = await _billingService.ListUserSubscriptionsAsync(
                user.Id,
                user.UserName ?? user.Email ?? user.Id,
                user.Email ?? $"{user.UserName}@eshoponweb.invalid",
                cancellationToken);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return ex.ToActionResult();
        }

        return new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionItemDto
            {
                SubscriptionId = s.Id,
                State = s.State,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceInCents = s.PriceInCents,
                NextBillingDateUtc = s.NextBillingDateUtc,
                CreatedAtUtc = s.CreatedAtUtc
            }).ToList()
        };
    }
}
