using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Ardalis.ApiEndpoints;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDetailDto? Subscription { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class SubscriptionDetailDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? State { get; set; }
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public decimal? PriceInDollars { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IAppLogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService service, IAppLogger<CreateSubscriptionEndpoint> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("api/subscriptions")]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Subscription creation attempt without authenticated user");
            response.Success = false;
            response.Message = "User not authenticated";
            return Unauthorized(response);
        }

        if (string.IsNullOrEmpty(request.PlanHandle))
        {
            response.Success = false;
            response.Message = "Plan handle is required";
            return BadRequest(response);
        }

        try
        {
            var subscription = await _service.CreateSubscriptionAsync(userId, request.PlanHandle, cancellationToken);

            response.Success = true;
            response.Message = "Subscription created successfully";
            response.Subscription = new SubscriptionDetailDto
            {
                Id = subscription?.Id,
                Handle = subscription?.Handle,
                State = subscription?.State,
                ProductId = subscription?.ProductId,
                ProductName = subscription?.ProductName,
                PriceInDollars = subscription?.PriceInCents.HasValue == true ? subscription.PriceInCents.Value / 100m : null,
                NextBillingDate = subscription?.NextBillingDate
            };

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Subscription creation failed: {ex.Message}");
            response.Success = false;
            response.Message = ex.Message;
            return BadRequest(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Unexpected error creating subscription: {ex.Message}");
            response.Success = false;
            response.Message = "An unexpected error occurred";
            return StatusCode(500, response);
        }
    }
}
