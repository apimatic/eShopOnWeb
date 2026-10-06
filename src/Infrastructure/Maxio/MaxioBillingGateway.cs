using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API over HTTPS with HTTP Basic
/// authentication (API key as username, literal "X" as password; configured on
/// the typed <see cref="HttpClient"/>). Requests target the site-scoped
/// resources documented by the Billing API: customers, subscriptions,
/// product families and their products.
/// </summary>
public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioBillingGateway(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetFamilyPlansAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle.Trim())}/products.json";
        var body = await GetAsync(path, cancellationToken);
        var wrappers = Deserialize<List<WireProductWrapper>>(body, path);

        return wrappers
            .Select(wrapper => wrapper.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Select(product => product!.ToPlan())
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        try
        {
            var body = await GetAsync(path, cancellationToken);
            return Deserialize<WireCustomerWrapper>(body, path).Customer?.ToCustomer();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        const string path = "customers.json";

        var wireRequest = new WireCreateCustomerRequest
        {
            Customer = new WireCreateCustomer
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.Reference,
                Organization = request.Organization,
            },
        };

        var body = await PostAsync(path, wireRequest, cancellationToken);
        return ExpectData(Deserialize<WireCustomerWrapper>(body, path).Customer, path).ToCustomer();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        const string path = "subscriptions.json";

        var wireRequest = new WireCreateSubscriptionRequest
        {
            Subscription = new WireCreateSubscription
            {
                CustomerId = request.CustomerId,
                ProductId = request.ProductId,
                Reference = request.Reference,
                PaymentCollectionMethod = request.PaymentCollectionMethod,
            },
            UniquenessToken = request.UniquenessToken,
        };

        var body = await PostAsync(path, wireRequest, cancellationToken);
        return ExpectData(Deserialize<WireSubscriptionWrapper>(body, path).Subscription, path).ToSubscription();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var body = await GetAsync(path, cancellationToken);
        var wrappers = Deserialize<List<WireSubscriptionWrapper>>(body, path);

        return wrappers
            .Select(wrapper => wrapper.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!.ToSubscription())
            .ToList();
    }

    private Task<string> GetAsync(string path, CancellationToken cancellationToken) =>
        ReadAsStringAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    private Task<string> PostAsync(string path, object jsonPayload, CancellationToken cancellationToken) =>
        ReadAsStringAsync(
            new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(jsonPayload, SerializerOptions),
                    Encoding.UTF8,
                "application/json"),
            },
            cancellationToken);

    private async Task<string> ReadAsStringAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new MaxioApiException(
                    0,
                    $"Maxio billing API unreachable ({request.Method} {request.RequestUri}): {ex.Message}");
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MaxioApiException(
                    0,
                    $"Maxio billing API request timed out ({request.Method} {request.RequestUri}): {ex.Message}");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errors = ParseErrors(body);
                    var summary = errors.Count > 0 ? string.Join(" ", errors) : Truncate(body);
                    throw new MaxioApiException(
                        (int)response.StatusCode,
                        $"Maxio billing API returned {(int)response.StatusCode} for {request.Method} {request.RequestUri}: {summary}",
                        errors);
                }

                return body;
            }
        }
    }

    private static T Deserialize<T>(string json, string path)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            return result ?? throw new JsonException("The response body was empty.");
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                0,
                $"Maxio billing API returned an unparseable response for {path}: {Truncate(json)}",
                new[] { ex.Message });
        }
    }

    private static T ExpectData<T>(T? data, string path)
    {
        return data ?? throw new MaxioApiException(
            0,
            $"Maxio billing API response for {path} did not contain the expected resource object.");
    }

    /// <summary>
    /// Billing API error payloads appear either as {"errors":["..."]} or as
    /// {"errors":{"field":"message"}}; some endpoints answer plain text.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return Array.Empty<string>();
            }

            return errorsElement.ValueKind switch
            {
                JsonValueKind.Array => errorsElement
                    .EnumerateArray()
                    .Select(item => item.ToString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList(),
                JsonValueKind.Object => errorsElement
                    .EnumerateObject()
                    .Select(property => $"{property.Name}: {property.Value}")
                    .ToList(),
                _ => new[] { errorsElement.ToString() },
            };
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "...";
}
