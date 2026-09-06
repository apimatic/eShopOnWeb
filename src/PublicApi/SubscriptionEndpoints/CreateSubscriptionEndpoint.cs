using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a new subscription
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly IOptions<MaxioConfiguration> _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioClient maxioClient,
        IOptions<MaxioConfiguration> config,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                response.Success = false;
                response.Message = "User not authenticated";
                return Unauthorized(response);
            }

            var email = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
            var lastName = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Surname)?.Value ?? userId;

            if (string.IsNullOrEmpty(email))
                email = $"{userId}@eshop.local";

            var customer = await _maxioClient.GetOrCreateCustomerAsync(userId, firstName, lastName, email);
            if (customer == null)
            {
                response.Success = false;
                response.Message = "Failed to create or retrieve customer from billing system";
                return BadRequest(response);
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(userId, request.ProductHandle);

            response.Success = true;
            response.Message = "Subscription created successfully";
            response.SubscriptionId = subscription.Id;
            response.CustomerId = subscription.CustomerId;
            response.State = subscription.State;
            response.ProductHandle = subscription.ProductHandle;
            response.NextBillingAt = subscription.NextBillingAt;
            response.CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt;

            return Ok(response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Failed to create subscription: {ex.Message}";
            return BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
