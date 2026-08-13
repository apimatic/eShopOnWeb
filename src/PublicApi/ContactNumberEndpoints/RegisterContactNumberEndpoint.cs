using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// POST /api/contact-numbers — register a mobile number for the signed-in shopper. A number the provider
/// does not consider a usable destination is rejected here; what is stored is the provider's canonical form.
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
                var ownerId = user.GetUserName();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return Results.BadRequest(new { message = "A phone number is required." });
                }

                try
                {
                    var contactNumber = await service.RegisterAsync(ownerId, request.PhoneNumber, cancellationToken);
                    return Results.Created($"api/contact-numbers/{contactNumber.Id}", new RegisterContactNumberResponse
                    {
                        ContactNumberId = contactNumber.Id,
                        PhoneNumber = contactNumber.PhoneNumber,
                        RegisteredAt = contactNumber.RegisteredAt
                    });
                }
                catch (InvalidPhoneNumberException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
