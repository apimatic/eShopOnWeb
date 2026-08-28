using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public ContactNumbersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(typeof(RegisterContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.RegisterContactNumberAsync(User.Identity!.Name!, request.MobileNumber, cancellationToken);
        if (!result.Succeeded)
        {
            return ToProblem(result.Error, result.Message!);
        }

        var response = new RegisterContactNumberResponse(result.Value!.Id);
        return Created($"/api/contact-numbers/{response.ContactNumberId}", response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ContactNumberDto[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var numbers = await _service.GetContactNumbersAsync(User.Identity!.Name!, cancellationToken);
        return Ok(numbers.Select(x => new ContactNumberDto(x.Id, x.CanonicalNumber, x.RegisteredAt)));
    }

    [HttpDelete("{contactNumberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteContactNumberAsync(User.Identity!.Name!, contactNumberId, cancellationToken);
        return result.Succeeded ? NoContent() : ToProblem(result.Error, result.Message!);
    }

    private ObjectResult ToProblem(OperationError error, string message) => Problem(
        statusCode: error switch
        {
            OperationError.Invalid => StatusCodes.Status400BadRequest,
            OperationError.NotFound => StatusCodes.Status404NotFound,
            OperationError.Conflict => StatusCodes.Status409Conflict,
            OperationError.ProviderUnavailable => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        },
        title: message);
}
