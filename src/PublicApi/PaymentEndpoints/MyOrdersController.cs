using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/my-orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MyOrdersController : ControllerBase
{
    private readonly PaymentService _payments;

    public MyOrdersController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderSummary>>> Get(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The request is not authenticated.");
        }

        var result = await _payments.GetMyOrdersAsync(buyerId, cancellationToken);
        return Ok(result);
    }
}
