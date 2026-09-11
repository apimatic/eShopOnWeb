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

public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
    .WithMetadata(new AuthorizeAttribute())
{
    private readonly IMaxioBillingService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IMaxioBillingService maxio, UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
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

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "List my subscriptions", OperationId = "subscriptions.mine", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = GetUserNameFromToken(_httpContextAccessor.HttpContext);
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            return Unauthorized();

        var refs = await _maxio.ListSubscriptionsAsync(user.Id);
        var response = new MySubscriptionsResponse(Guid.NewGuid());
        foreach (var s in refs)
        {
            response.Subscriptions.Add(new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductHandle = s.ProductHandle,
                ProductName = s.ProductName,
                Price = s.PriceInCents,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt
            });
        }
        return Ok(response);
    }
}
