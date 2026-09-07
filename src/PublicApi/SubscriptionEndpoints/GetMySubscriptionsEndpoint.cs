using System;
using System.Collections.Generic;
using System.Linq;
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

public class GetMySubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<GetMySubscriptionsEndpoint.Response>
{
    private readonly IMapper _mapper;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMaxioSubscriptionService _service;

    public GetMySubscriptionsEndpoint(
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

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Get user's subscriptions", Tags = new[] { "SubscriptionEndpoints" })]
    [Authorize]
    public override async Task<ActionResult<Response>> HandleAsync(CancellationToken cancellationToken = default)
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

            var subscriptions = await _service.GetCustomerSubscriptionsAsync(customerId, httpContext.RequestAborted);

            var response = new Response
            {
                Subscriptions = subscriptions.Select(s => new SubscriptionInfoDto
                {
                    Id = s.Id,
                    CustomerId = s.CustomerId,
                    State = s.State,
                    ProductPrice = s.ProductPriceInCents / 100m,
                    CreatedAt = s.CreatedAt,
                    NextBillingAt = s.NextBillingAt
                }).ToList()
            };

            return Ok(response);
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

    public class Response
    {
        public List<SubscriptionInfoDto> Subscriptions { get; set; } = new();
    }
}
