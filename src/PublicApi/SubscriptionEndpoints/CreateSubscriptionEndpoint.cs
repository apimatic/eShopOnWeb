using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<SubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billing;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billing) => _billing = billing;

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Enrolls the authenticated user in a Maxio subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default) =>
        Ok(await _billing.SubscribeAsync(User, request.ProductHandle, cancellationToken));
}
