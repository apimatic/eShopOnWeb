using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's numbers. Afterwards it no longer appears among the
/// caller's numbers and nothing is sent to it again. A number that is not the caller's is not found.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeleteContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult
{
    private readonly IContactNumberService _contactNumberService;

    public DeleteContactNumberEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    [HttpDelete("api/contact-numbers/{contactNumberId}")]
    [SwaggerOperation(
        Summary = "Removes one of the signed-in shopper's numbers",
        Description = "Removes one of the signed-in shopper's numbers",
        OperationId = "contactNumbers.delete",
        Tags = new[] { "ContactNumberEndpoints" })]
    public override async Task<ActionResult> HandleAsync([FromRoute] int contactNumberId, CancellationToken cancellationToken = default)
    {
        var ownerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Unauthorized();
        }

        var removed = await _contactNumberService.RemoveAsync(ownerId, contactNumberId, cancellationToken);
        return removed ? NoContent() : NotFound();
    }
}
