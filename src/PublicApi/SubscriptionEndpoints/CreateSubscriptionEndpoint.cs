using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in the requested subscription plan.
/// Idempotent: repeated calls with the same plan never create duplicate
/// customers or duplicate live subscriptions.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly SubscriptionUserContext _userContext;
    private readonly ISubscriptionBillingService _billingService;

    public CreateSubscriptionEndpoint(SubscriptionUserContext userContext, ISubscriptionBillingService billingService)
    {
        _userContext = userContext;
        _billingService = billingService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a billing customer exists for the user, then enrolls them in the requested plan. Idempotent on retry.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userContext.GetApplicationUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return BadRequest(new { errors = new[] { "planHandle is required." } });
        }

        SubscribeResult result;
        try
        {
            result = await _billingService.SubscribeAsync(
                user.Id,
                user.UserName ?? user.Email ?? user.Id,
                user.Email ?? $"{user.UserName}@eshoponweb.invalid",
                request.PlanHandle,
                cancellationToken);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return ex.ToActionResult();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Created = result.CreatedNew,
            Subscription = new SubscriptionItemDto
            {
                SubscriptionId = result.Subscription.Id,
                State = result.Subscription.State,
                PlanHandle = result.Subscription.PlanHandle,
                PlanName = result.Subscription.PlanName,
                PriceInCents = result.Subscription.PriceInCents,
                NextBillingDateUtc = result.Subscription.NextBillingDateUtc,
                CreatedAtUtc = result.Subscription.CreatedAtUtc
            }
        };

        return result.CreatedNew
            ? new ObjectResult(response) { StatusCode = StatusCodes.Status201Created }
            : Ok(response);
    }
}
