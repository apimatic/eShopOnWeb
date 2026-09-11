using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly ISubscriptionService _service;
    public SubscribeEndpoint(ISubscriptionService service) => _service = service;

    [Authorize]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var result = await _service.SubscribeAsync(userName, userName, request.PlanHandle);
        if (!result.Success)
            return BadRequest(new SubscribeResponse(false, result.Message, null, null, null));

        return Ok(new SubscribeResponse(true, result.Message, result.PlanHandle, result.State, result.NextBillingDate));
    }
}

public class SubscribeRequest
{
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}

public record SubscribeResponse(bool Success, string Message, string? PlanHandle, string? State, string? NextBillingDate);
