using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/my-orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MyOrdersController : ControllerBase
{
    private readonly CommerceService _service;
    public MyOrdersController(CommerceService service) => _service = service;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> Get(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name
            ?? throw new UnauthorizedAccessException("The bearer token has no name claim.");
        return Ok(await _service.GetMyOrdersAsync(buyerId, cancellationToken));
    }
}
