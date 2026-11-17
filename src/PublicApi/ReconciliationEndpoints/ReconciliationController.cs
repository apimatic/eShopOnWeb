using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

[ApiController]
[Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ReconciliationController : ControllerBase
{
    private readonly ReconciliationService _reconciliationService;

    public ReconciliationController(ReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    [HttpGet("api/reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> Get(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue)
        {
            throw new PaymentValidationException(
                "from and to are required ISO-8601 date-times, for example " +
                "2026-11-01T00:00:00Z and 2026-11-30T23:59:59Z.");
        }

        return Ok(await _reconciliationService.GetAsync(from.Value, to.Value, cancellationToken));
    }
}
