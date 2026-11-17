using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MyOrdersController : ControllerBase
{
    private readonly PaymentService _payments;

    public MyOrdersController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<MyOrderResponse>>> Get(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Unauthorized();

        return Ok(await _payments.GetMyOrdersAsync(buyerId, cancellationToken));
    }
}
