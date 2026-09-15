using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// POST /api/contact-numbers — register a mobile number for the signed-in shopper. A number the provider does
/// not consider a usable destination is rejected here (not when a later message fails), and what gets stored is
/// the provider's own canonical form of the number.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ContactNumberEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ContactNumberEndpointServices services) =>
                await HandleAsync(request, services))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ContactNumberEndpointServices services)
    {
        var buyerId = services.User.UserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { message = "A phone number is required." });

        // Reject an unusable destination at registration, and keep the provider's canonical form.
        var validation = await services.PhoneNumberValidator.ValidateAsync(request.PhoneNumber);
        if (!validation.IsValid || validation.CanonicalNumber is null)
            return Results.BadRequest(new { message = validation.Reason ?? "The number is not a valid, reachable destination." });

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        contactNumber = await services.ContactNumbers.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
