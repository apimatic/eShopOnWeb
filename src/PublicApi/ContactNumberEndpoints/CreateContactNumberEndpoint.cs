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
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// provider at registration time and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IContactNumberService _contactNumberService;

    public CreateContactNumberEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (buyerId is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest();
        }

        try
        {
            var contactNumber = await _contactNumberService.RegisterAsync(buyerId, request.PhoneNumber);
            var response = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = contactNumber.Id,
                PhoneNumber = contactNumber.PhoneNumber,
                CreatedAt = contactNumber.CreatedAt
            };
            return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
        }
        catch (InvalidPhoneNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (SmsProviderException)
        {
            return Results.Problem("The messaging provider could not be reached; the number was not registered.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
