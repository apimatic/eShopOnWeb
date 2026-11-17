using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(
    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly PaymentService _payments;

    public ReconciliationController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpGet("api/reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> Get(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (from is null || to is null)
            throw PaymentException.BadRequest("DATE_RANGE_REQUIRED", "Both 'from' and 'to' ISO-8601 date-times are required.");

        return Ok(await _payments.ReconcileAsync(from.Value, to.Value, cancellationToken));
    }
}
