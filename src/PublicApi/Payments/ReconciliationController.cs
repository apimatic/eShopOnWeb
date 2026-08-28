using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/reconciliation")]
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

    [HttpGet]
    [ProducesResponseType(typeof(ReconciliationResponse), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<ReconciliationResponse>> Get(
        [FromQuery(Name = "from")] DateTimeOffset from,
        [FromQuery(Name = "to")] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return Ok(await _payments.ReconcileAsync(from, to, cancellationToken));
    }
}
