using System;
using System.Text.Json.Serialization;
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
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; what is stored is the provider's canonical E.164 form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IContactNumberService service, CancellationToken ct) =>
            {
                var caller = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(caller))
                {
                    return Results.Unauthorized();
                }
                request.CallerId = caller;
                return await HandleAsync(request, service, ct);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Number))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var result = await service.RegisterAsync(request.CallerId, request.Number, ct);
        if (result.Outcome == ContactNumberRegistrationOutcome.Rejected || result.ContactNumber is null)
        {
            return Results.BadRequest(new { message = result.RejectReason ?? "The number is not a usable destination." });
        }

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumber.Id,
            E164Number = result.ContactNumber.E164Number
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number as the caller typed it; validated and canonicalized by the provider.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>The signed-in shopper, taken from the token — never from the request body.</summary>
    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public RegisterContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string E164Number { get; set; } = string.Empty;
}
