using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. A number the
/// provider does not consider a usable destination is rejected here; the canonical form is stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                ClaimsPrincipal user,
                IContactNumberService service,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var result = await service.RegisterAsync(buyerId, request?.PhoneNumber ?? string.Empty, cancellationToken);
                if (result.Outcome == RegisterOutcome.Rejected)
                {
                    return Results.BadRequest(new { error = result.RejectionReason });
                }

                var contactNumber = result.ContactNumber!;
                return Results.Created(
                    $"api/contact-numbers/{contactNumber.Id}",
                    new RegisterContactNumberResponse(contactNumber.Id, contactNumber.PhoneNumber));
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
