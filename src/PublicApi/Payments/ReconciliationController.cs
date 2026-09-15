using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly PaymentWorkflowService _workflow;

    public ReconciliationController(PaymentWorkflowService workflow) => _workflow = workflow;

    [HttpGet("api/reconciliation")]
    [ProducesResponseType(typeof(ReconciliationResponse), 200)]
    public async Task<ActionResult<ReconciliationResponse>> Get([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(await _workflow.ReconcileAsync(from, to, cancellationToken));
}
