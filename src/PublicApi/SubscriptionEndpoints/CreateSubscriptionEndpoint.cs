using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioService maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribe to a plan",
        Description = "Creates or returns existing subscription for the logged-in user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Unauthorized();
        }
        var result = await _maxio.SubscribeAsync(
            user.Id, user.Email ?? userName, "User", "Shop", request.ProductHandle);
        response.Subscription = new SubscriptionResultDto
        {
            SubscriptionId = result.SubscriptionId,
            State = result.State,
            PlanName = result.PlanName,
            PlanHandle = result.PlanHandle,
            PriceInCents = result.PriceInCents,
            Currency = result.Currency,
            NextBillingDate = result.NextBillingDate
        };
        return Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "eshop-pro";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionResultDto Subscription { get; set; } = new();
}

public class SubscriptionResultDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string NextBillingDate { get; set; } = string.Empty;
}
