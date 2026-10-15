using System.ComponentModel.DataAnnotations;
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

public class SubscribeRequest
{
    [Required, StringLength(255)]
    public string ProductHandle { get; set; } = "";
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint(ISubscriptionBilling billing, UserManager<ApplicationUser> users)
    : EndpointBaseAsync.WithRequest<SubscribeRequest>.WithActionResult<SubscriptionEnrollment>
{
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan; repeated requests return the existing subscription", Tags = new[] { "Subscriptions" })]
    [ProducesResponseType(typeof(SubscriptionEnrollment), 200)]
    [ProducesResponseType(typeof(SubscriptionEnrollment), 202)]
    public override Task<ActionResult<SubscriptionEnrollment>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default) =>
        SubscriptionEndpointSupport.Execute<SubscriptionEnrollment>(this, users, async user =>
        {
            if (string.IsNullOrWhiteSpace(user.Email)) return BadRequest(new ProblemDetails { Title = "An account email is required." });
            var result = await billing.SubscribeAsync(user.Id, user.Email, request.ProductHandle, cancellationToken);
            if (result.Status == "pending")
            {
                Response.Headers.RetryAfter = "30";
                return Accepted("/api/my-subscriptions", result);
            }
            return Ok(result);
        });
}
