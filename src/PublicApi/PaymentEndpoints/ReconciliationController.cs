using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/reconciliation")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly PaymentWorkflow _workflow;

    public ReconciliationController(PaymentWorkflow workflow) => _workflow = workflow;

    [HttpGet]
    public async Task<ActionResult<ReconciliationResponse>> Get([FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue)
            throw new ApiProblemException(400, "DATE_RANGE_REQUIRED", "Both from and to are required ISO-8601 date-times.");
        return Ok(await _workflow.ReconcileAsync(from.Value, to.Value, cancellationToken));
    }
}
