using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Lists the authenticated shopper's subscriptions",
        OperationId = "subscriptions.listMine", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var user = await BillingEndpointSupport.GetBillingUserAsync(User, _userManager);
        if (user is null) return Unauthorized();

        try
        {
            var subscriptions = await _billingService.GetSubscriptionsAsync(user, cancellationToken);
            return Ok(new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList()
            });
        }
        catch (MaxioApiException ex)
        {
            return StatusCode(ex.IsTransient ? 503 : 502,
                new ProblemDetails { Title = "Billing service unavailable", Detail = ex.Message });
        }
    }
}
