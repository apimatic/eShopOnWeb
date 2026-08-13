using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here; what is stored is the provider's canonical form of the number.
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
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                if (string.IsNullOrWhiteSpace(request?.PhoneNumber))
                {
                    return Results.BadRequest(new { message = "A phone number is required." });
                }

                var contactNumber = await service.RegisterAsync(buyerId, request.PhoneNumber, cancellationToken);
                if (contactNumber == null)
                {
                    return Results.BadRequest(new { message = "The number provided is not a usable destination and was rejected." });
                }

                return Results.Created($"api/contact-numbers/{contactNumber.Id}", new ContactNumberDto
                {
                    ContactNumberId = contactNumber.Id,
                    PhoneNumber = contactNumber.PhoneNumber,
                    RegisteredDate = contactNumber.RegisteredDate
                });
            })
            .Produces<ContactNumberDto>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }
}
