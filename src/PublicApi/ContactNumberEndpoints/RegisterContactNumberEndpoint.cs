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
/// Registers a mobile number for the signed-in shopper. The provider validates the number; an unusable
/// destination is rejected here, and the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, HttpContext>
{
    private readonly IContactNumberService _service;

    public RegisterContactNumberEndpoint(IContactNumberService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http) =>
            {
                return await HandleAsync(request, http);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, HttpContext http)
    {
        var buyerId = CallerIdentity.Of(http.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        var response = new RegisterContactNumberResponse(request.CorrelationId());
        try
        {
            var contactNumber = await _service.RegisterAsync(buyerId, request.PhoneNumber, http.RequestAborted);
            if (contactNumber is null)
            {
                return Results.BadRequest("The number provided is not a usable SMS destination.");
            }

            response.ContactNumberId = contactNumber.Id;
            response.ContactNumber = ContactNumberDto.From(contactNumber);
            return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
        }
        catch (SmsProviderException ex)
        {
            // Could not reach the provider to validate the number — surface as an upstream failure.
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
