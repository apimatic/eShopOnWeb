using System.Net;
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

public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribes the authenticated shopper to a plan",
        OperationId = "subscriptions.create", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionResponse>> HandleAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await BillingEndpointSupport.GetBillingUserAsync(User, _userManager);
        if (user is null) return Unauthorized();

        try
        {
            var result = await _billingService.SubscribeAsync(user, request.ProductHandle, cancellationToken);
            var response = new SubscriptionResponse
            {
                Created = result.Created,
                Subscription = SubscriptionDto.From(result.Subscription)
            };
            return result.Created ? Created("/api/my-subscriptions", response) : Ok(response);
        }
        catch (BillingPlanNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Plan not found", Detail = ex.Message });
        }
        catch (SubscriptionProvisioningInProgressException ex)
        {
            return Conflict(new ProblemDetails { Title = "Subscription is being processed", Detail = ex.Message });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Subscription was rejected", Detail = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return StatusCode(ex.IsTransient ? 503 : 502,
                new ProblemDetails { Title = "Billing service unavailable", Detail = ex.Message });
        }
    }
}
