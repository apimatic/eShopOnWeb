using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

[ApiController]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly PaymentWorkflowService _payments;

    public ReconciliationController(PaymentWorkflowService payments) => _payments = payments;

    [HttpGet("api/reconciliation")]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public Task<ReconciliationResponse> Get([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) => _payments.ReconcileAsync(from, to, cancellationToken);
}
