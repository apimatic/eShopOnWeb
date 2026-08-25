using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: if the user already holds
/// the plan, the existing subscription is returned instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated user and enrolls them in the given plan. Safe to retry: an existing live subscription for the same plan is returned unchanged.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
        if (user is null)
        {
            return Unauthorized();
        }

        var subscription = await _billingService.SubscribeAsync(
            user.Id, user.Email!, user.UserName, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(subscription)
        };

        return Ok(response);
    }

    internal static SubscriptionDto ToDto(CustomerSubscription s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        PaymentCollectionMethod = s.PaymentCollectionMethod,
        ActivatedAt = s.ActivatedAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingAt = s.NextBillingAt
    };
}
