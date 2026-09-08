using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: repeating the
/// request returns the existing subscription instead of creating a second one.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionCreateEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionCreateEndpoint(ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes to a plan",
        Description = "Ensures a billing customer exists for the user and subscribes them to the given plan (the default plan when none is given)",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return Problem(title: "Subscription billing error",
                detail: "Your account has no email address, which the billing provider requires.",
                statusCode: Microsoft.AspNetCore.Http.StatusCodes.Status422UnprocessableEntity);
        }

        ApplicationCore.Models.SubscribeResult result;
        try
        {
            result = await _billingService.SubscribeAsync(user.Id, user.Email, userName,
                request?.PlanHandle, cancellationToken);
        }
        catch (MaxioBillingException ex)
        {
            return ex.ToActionResult();
        }

        return new SubscribeResponse(request!.CorrelationId())
        {
            Subscription = ToDto(result.Subscription),
            Created = result.Created
        };
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Models.SubscriptionInfo info) => new SubscriptionDto
    {
        SubscriptionId = info.SubscriptionId,
        Reference = info.Reference,
        PlanHandle = info.PlanHandle,
        PlanName = info.PlanName,
        PriceInCents = info.PriceInCents,
        Interval = info.Interval,
        IntervalUnit = info.IntervalUnit,
        State = info.State,
        IsActive = info.IsActive,
        NextBillingDate = info.NextBillingDate,
        CurrentPeriodEndsAt = info.CurrentPeriodEndsAt
    };
}
