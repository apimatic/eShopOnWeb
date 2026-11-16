using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/reconciliation")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ReconciliationController : ControllerBase
{
    private readonly PaymentService _payments;

    public ReconciliationController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpGet]
    public async Task<ActionResult<ReconciliationReport>> Get([FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue)
        {
            throw new PaymentOperationException(400,
                "Both 'from' and 'to' ISO-8601 date-times are required, for example ?from=2026-01-01T00:00:00Z&to=2026-12-31T23:59:59Z.");
        }

        var result = await _payments.ReconcileAsync(from.Value, to.Value, cancellationToken);
        return Ok(result);
    }
}
