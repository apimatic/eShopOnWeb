using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public ContactNumbersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(typeof(RegisterContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RegisterContactNumberResponse>> Register(
        RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.RegisterContactNumberAsync(BuyerId(), request,
            cancellationToken);
        return Created($"/api/contact-numbers/{response.ContactNumberId}", response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ContactNumberListResponse), StatusCodes.Status200OK)]
    public Task<ContactNumberListResponse> List(CancellationToken cancellationToken) =>
        _service.GetContactNumbersAsync(BuyerId(), cancellationToken);

    [HttpDelete("{contactNumberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int contactNumberId,
        CancellationToken cancellationToken)
    {
        await _service.DeleteContactNumberAsync(BuyerId(), contactNumberId, cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name) ??
        throw new NotificationApiException(StatusCodes.Status401Unauthorized,
            "The token does not contain a shopper identity.");
}
