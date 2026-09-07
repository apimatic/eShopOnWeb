using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<GetSubscriptionPlansEndpoint.Response>
{
    private readonly IMapper _mapper;
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetSubscriptionPlansEndpoint(IMapper mapper, IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _mapper = mapper;
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List available subscription plans", Tags = new[] { "SubscriptionEndpoints" })]
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

            var plans = await _service.GetAvailablePlansAsync(httpContext.RequestAborted);
            var response = new Response
            {
                Plans = plans.Select(_mapper.Map<SubscriptionPlanDto>).ToList()
            };
            return Ok(response);
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }

    public class Response
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }
}
