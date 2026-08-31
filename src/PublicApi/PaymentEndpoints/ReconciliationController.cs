using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly PaymentApplicationService _payments;

    public ReconciliationController(PaymentApplicationService payments) => _payments = payments;

    [HttpGet("api/reconciliation")]
    [SwaggerOperation(Summary = "Reconciles PayPal reporting with eShop payments", Tags = new[] { "PaymentEndpoints" })]
    public Task<ReconciliationResponse> Get([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) => _payments.ReconcileAsync(from, to, cancellationToken);
}
