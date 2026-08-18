using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalise.</summary>
    public string Number { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) { }

    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
    public System.DateTimeOffset RegisteredDate { get; set; }
}

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. An unusable
/// destination is rejected here (400); what is stored is the provider's canonical E.164 form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Number))
        {
            return Results.BadRequest(new { message = "A number is required." });
        }

        var service = http.RequestServices.GetRequiredService<IContactNumberService>();

        try
        {
            var contactNumber = await service.RegisterAsync(buyerId, request.Number, http.RequestAborted);
            var response = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = contactNumber.Id,
                E164Number = contactNumber.E164Number,
                RegisteredDate = contactNumber.RegisteredDate
            };
            return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
        }
        catch (InvalidPhoneNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message, validationErrors = ex.ValidationErrors });
        }
    }
}
