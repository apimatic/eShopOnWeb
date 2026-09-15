using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/reconciliation")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly CommerceService _service;
    public ReconciliationController(CommerceService service) => _service = service;

    [HttpGet]
    [ProducesResponseType<ReconciliationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Get(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Ok(await _service.ReconcileAsync(from, to, cancellationToken));
}
