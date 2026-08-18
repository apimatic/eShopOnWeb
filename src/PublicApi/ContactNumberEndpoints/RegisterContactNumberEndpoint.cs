using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated and normalised by
/// the provider; an unusable number is rejected here, and the provider's canonical E.164 form is
/// what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberCommand, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var ownerId = user.UserName();
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
                if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return Results.BadRequest(new { error = "A phone number is required." });
                }
                return await HandleAsync(new RegisterContactNumberCommand(ownerId, request.PhoneNumber, request.CountryCode), service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberCommand request, IOrderNotificationService service)
    {
        try
        {
            var contactNumber = await service.RegisterContactNumberAsync(request.OwnerId, request.PhoneNumber, request.CountryCode);
            var dto = ContactNumberDto.From(contactNumber);
            var response = new RegisterContactNumberResponse { ContactNumberId = dto.ContactNumberId, ContactNumber = dto };
            return Results.Created($"api/contact-numbers/{dto.ContactNumberId}", response);
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { error = ex.Message, validationErrors = ex.ValidationErrors });
        }
    }
}
