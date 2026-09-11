using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IMaxioService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(IMaxioService maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "List my subscriptions",
        Description = "Returns subscriptions for the authenticated user",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetMySubscriptionsResponse(Guid.NewGuid());
        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName)) return Unauthorized();
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null) return Unauthorized();
        var subs = await _maxio.GetMySubscriptionsAsync(user.Id);
        response.Subscriptions = subs.Select(s => new SubscriptionResultDto
        {
            SubscriptionId = s.SubscriptionId,
            State = s.State,
            PlanName = s.PlanName,
            PlanHandle = s.PlanHandle,
            PriceInCents = s.PriceInCents,
            Currency = s.Currency,
            NextBillingDate = s.NextBillingDate
        }).ToList();
        return Ok(response);
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionResultDto> Subscriptions { get; set; } = new();
}
