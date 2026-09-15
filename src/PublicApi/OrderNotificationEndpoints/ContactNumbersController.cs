using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly OrderNotificationCoordinator _coordinator;

    public ContactNumbersController(OrderNotificationCoordinator coordinator) => _coordinator = coordinator;

    [HttpPost]
    public async Task<IResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var contact = await _coordinator.RegisterContactNumberAsync(UserName(), request.PhoneNumber, cancellationToken);
            if (contact is null) return Results.BadRequest(new { error = "The phone number is not a usable destination." });
            return Results.Created($"/api/contact-numbers/{contact.Id}", new
            {
                contactNumberId = contact.Id,
                phoneNumber = contact.PhoneNumber
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Results.Problem("The phone number could not be validated with the messaging provider.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpGet]
    public async Task<IResult> List(CancellationToken cancellationToken)
    {
        var contacts = await _coordinator.ListContactNumbersAsync(UserName(), cancellationToken);
        return Results.Ok(new
        {
            contactNumbers = contacts.Select(x => new
            {
                contactNumberId = x.Id,
                phoneNumber = x.PhoneNumber,
                createdAt = x.CreatedAt
            })
        });
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var deleted = await _coordinator.DeleteContactNumberAsync(UserName(), contactNumberId, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private string UserName() => User.Identity?.Name ?? throw new UnauthorizedAccessException();
}

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}
