using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
    .WithMetadata(new AuthorizeAttribute())
{
    private readonly IMaxioBillingService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IMaxioBillingService maxio, UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _maxio = maxio;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    private static string? GetUserNameFromToken(HttpContext? httpContext)
    {
        var auth = httpContext?.Request?.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = auth.Substring("Bearer ".Length).Trim();
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            var pad = 4 - payload.Length % 4;
            if (pad != 4) payload += new string('=', pad);
            var bytes = Convert.FromBase64String(payload);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("unique_name", out var un)) return un.GetString();
            return doc.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }
        catch { return null; }
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", OperationId = "subscriptions.create", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var userName = GetUserNameFromToken(_httpContextAccessor.HttpContext);
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            return Unauthorized();

        var reference = user.Id;
        var email = user.Email ?? user.UserName ?? "unknown@eshop.com";
        var firstName = "User";
        var lastName = "Name";

        await _maxio.EnsureCustomerAsync(reference, email, firstName, lastName);

        var handle = !string.IsNullOrWhiteSpace(request.ProductHandle) ? request.ProductHandle : "eshop-pro";
        var sub = await _maxio.SubscribeAsync(reference, handle);

        var response = new CreateSubscriptionResponse(Guid.NewGuid())
        {
            SubscriptionId = sub.Id,
            State = sub.State,
            ProductHandle = sub.ProductHandle,
            ProductName = sub.ProductName,
            Price = sub.PriceInCents,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt
        };
        return Ok(response);
    }
}
