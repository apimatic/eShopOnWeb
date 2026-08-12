using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider up
/// front — an unusable destination is rejected here, not when a later message fails — and the
/// provider's canonical E.164 form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, HttpContext, IRepository<ContactNumber>>
{
    private readonly IPhoneNumberValidator _validator;

    public RegisterContactNumberEndpoint(IPhoneNumberValidator validator)
    {
        _validator = validator;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IRepository<ContactNumber> repository) =>
            {
                return await HandleAsync(request, http, repository);
            })
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, HttpContext http, IRepository<ContactNumber> repository)
    {
        var response = new RegisterContactNumberResponse(request.CorrelationId());

        var buyerId = http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { errors = new[] { "A phone number is required." } });

        var validation = await _validator.ValidateAsync(request.PhoneNumber);
        if (!validation.IsValid)
            return Results.BadRequest(new { message = "The phone number is not a usable destination.", errors = validation.Errors });

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber!);
        contactNumber = await repository.AddAsync(contactNumber);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
