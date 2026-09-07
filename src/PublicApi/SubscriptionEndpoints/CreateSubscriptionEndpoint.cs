using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync.WithRequest<CreateSubscriptionEndpoint.Request>.WithActionResult<CreateSubscriptionEndpoint.Response>
{
    private readonly IMapper _mapper;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMaxioSubscriptionService _service;

    public CreateSubscriptionEndpoint(
        IMapper mapper,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        IMaxioSubscriptionService service)
    {
        _mapper = mapper;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _service = service;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Create a new subscription", Tags = new[] { "SubscriptionEndpoints" })]
    [Authorize]
    public override async Task<ActionResult<Response>> HandleAsync(Request request, CancellationToken cancellationToken = default)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return StatusCode(500);
            }

            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest(new { error = "User not authenticated" });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            var (customerId, _) = await _service.EnsureCustomerAsync(
                userId, user.Email!, user.UserName ?? "User", "", httpContext.RequestAborted);

            var subscription = await _service.CreateSubscriptionAsync(
                customerId, request.ProductHandle, userId, httpContext.RequestAborted);

            var response = new Response
            {
                SubscriptionId = subscription.Id,
                CustomerId = subscription.CustomerId,
                State = subscription.State,
                ProductPrice = subscription.ProductPriceInCents / 100m,
                CreatedAt = subscription.CreatedAt,
                NextBillingAt = subscription.NextBillingAt
            };

            return CreatedAtAction(nameof(CreateSubscriptionEndpoint), new { id = subscription.Id }, response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }

    public class Request
    {
        public string ProductHandle { get; set; } = string.Empty;
    }

    public class Response
    {
        public int SubscriptionId { get; set; }
        public int CustomerId { get; set; }
        public string State { get; set; } = string.Empty;
        public decimal ProductPrice { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? NextBillingAt { get; set; }
    }
}
