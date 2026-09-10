using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Ensures a Maxio customer exists (idempotent) and enrolls
/// them. Idempotent overall: a live subscription to the same plan is returned rather than duplicated.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billingService;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer for the authenticated user and subscribes them to the given plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        [FromBody] SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new SubscriptionErrorResponse { StatusCode = 400, Message = "A planHandle is required." });
        }

        // Identity comes from the JWT: the name claim carries the eShopOnWeb username (an email).
        var username = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var subscriber = BuildSubscriber(username, request);

        try
        {
            var result = await _billingService.SubscribeAsync(subscriber, request.PlanHandle.Trim(), cancellationToken);
            return Ok(ToResponse(result));
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionErrorMapper.ToActionResult(ex);
        }
    }

    private static SubscriberIdentity BuildSubscriber(string username, SubscribeRequest request)
    {
        // The username is the stable per-user key (used as the Maxio customer reference) and the email.
        // Maxio requires a first and last name; default them from the email local part when not supplied.
        var localPart = username.Contains('@') ? username[..username.IndexOf('@')] : username;
        var firstName = string.IsNullOrWhiteSpace(request.FirstName) ? localPart : request.FirstName!.Trim();
        var lastName = string.IsNullOrWhiteSpace(request.LastName) ? "eShop Subscriber" : request.LastName!.Trim();
        return new SubscriberIdentity(Reference: username, Email: username, FirstName: firstName, LastName: lastName);
    }

    private static CreateSubscriptionResponse ToResponse(SubscribeResult result) => new()
    {
        SubscriptionId = result.SubscriptionId,
        CustomerId = result.CustomerId,
        PlanHandle = result.PlanHandle,
        PlanName = result.PlanName,
        PriceInCents = result.PriceInCents,
        State = result.State,
        NextBillingDate = result.NextBillingDate,
        AlreadySubscribed = result.AlreadySubscribed
    };
}
