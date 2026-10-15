using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint(ISubscriptionBilling billing, UserManager<ApplicationUser> users)
    : EndpointBaseAsync.WithoutRequest.WithActionResult<SubscriptionAccount>
{
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Read the authenticated shopper's current subscriptions from Maxio", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<SubscriptionAccount>> HandleAsync(CancellationToken cancellationToken = default) =>
        SubscriptionEndpointSupport.Execute<SubscriptionAccount>(this, users, async user => Ok(await billing.GetSubscriptionsAsync(user.Id, cancellationToken)));
}
