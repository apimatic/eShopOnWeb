using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionCreateEndpoint : EndpointBaseAsync
    .WithRequest<SubscriptionCreateRequest>
    .WithActionResult<SubscriptionCreateResponse>
{
    private readonly IMaxioService _maxioService;

    public SubscriptionCreateEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Creates a new subscription",
        Description = "Creates a new subscription in Maxio Advanced Billing for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionCreateResponse>> HandleAsync(
        SubscriptionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var userReference = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userReference))
        {
            return Unauthorized();
        }

        var firstName = User.FindFirstValue("given_name") ?? User.FindFirstValue("first_name") ?? userReference;
        var lastName = User.FindFirstValue("family_name") ?? User.FindFirstValue("last_name") ?? "";
        var email = User.FindFirstValue(ClaimTypes.Email) ?? $"{userReference}@eshop.com";

        var result = await _maxioService.CreateSubscriptionAsync(
            request.ProductHandle,
            userReference,
            firstName,
            lastName,
            email,
            cancellationToken);

        return Ok(new SubscriptionCreateResponse
        {
            SubscriptionId = result.SubscriptionId,
            State = result.State,
            CurrentPeriodEndsAt = result.CurrentPeriodEndsAt,
            NextAssessmentAt = result.NextAssessmentAt,
            ProductHandle = result.ProductHandle,
            ProductName = result.ProductName,
            PriceInCents = result.PriceInCents
        });
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse() { }
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId) { }

    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
}
