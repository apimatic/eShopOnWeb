using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<SubscriptionDto>
{
    private readonly IRecurringSubscriptionService _subscriptions;
    private readonly IAuthenticatedBillingUserProvider _userProvider;

    public CreateSubscriptionEndpoint(
        IRecurringSubscriptionService subscriptions,
        IAuthenticatedBillingUserProvider userProvider)
    {
        _subscriptions = subscriptions;
        _userProvider = userProvider;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current shopper to a recurring plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionDto>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userProvider.GetAsync(User, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var subscription = SubscriptionDto.From(await _subscriptions.SubscribeAsync(
                user,
                request.ProductHandle,
                cancellationToken));
            return subscription.IsPending ? Accepted(subscription) : Ok(subscription);
        }
        catch (BillingValidationException exception)
        {
            return SubscriptionEndpointErrors.ToActionResult<SubscriptionDto>(this, exception);
        }
        catch (BillingProviderException exception)
        {
            return SubscriptionEndpointErrors.ToActionResult<SubscriptionDto>(this, exception);
        }
    }
}
