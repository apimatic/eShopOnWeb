using System;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Set from the token, not the body.</summary>
    internal string BuyerId { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the newly registered number (top-level, so the caller can act on it).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; what gets stored is the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IContactNumberService service) =>
            {
                request.BuyerId = http.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterAsync(request.BuyerId, request.Number);
        if (result.Status == ResultStatus.Invalid)
        {
            return Results.ValidationProblem(result.ValidationErrors.ToDictionary(
                e => string.IsNullOrEmpty(e.Identifier) ? "number" : e.Identifier,
                e => new[] { e.ErrorMessage }));
        }

        var contactNumber = result.Value;
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedDate = contactNumber.CreatedDate
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
