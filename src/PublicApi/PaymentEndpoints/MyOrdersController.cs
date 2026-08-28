using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/my-orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MyOrdersController : ControllerBase
{
    private readonly IPaymentService _payments;
    public MyOrdersController(IPaymentService payments) => _payments = payments;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderPaymentView>>> Get(CancellationToken cancellationToken) =>
        Ok(await _payments.GetOrdersAsync(User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            cancellationToken));
}
