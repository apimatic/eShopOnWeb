using System.Collections.Generic;
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
public class ListSubscriptionPlansEndpoint(ISubscriptionBilling billing, UserManager<ApplicationUser> users)
    : EndpointBaseAsync.WithoutRequest.WithActionResult<IReadOnlyList<SubscriptionPlan>>
{
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List subscription plans in the configured family", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<IReadOnlyList<SubscriptionPlan>>> HandleAsync(CancellationToken cancellationToken = default) =>
        SubscriptionEndpointSupport.Execute<IReadOnlyList<SubscriptionPlan>>(this, users, async _ => Ok(await billing.GetPlansAsync(cancellationToken)));
}
