using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Core.Request;

internal sealed class FormRequest : IRequest
{
    private readonly IReadOnlyCollection<MultipartParam> _params;

    private FormRequest(IReadOnlyCollection<MultipartParam> @params) => _params = @params;

    public HttpContent Get()
    {
        var multipart = new MultipartFormDataContent();
        foreach (var param in _params)
            AddPart(multipart, param);
        return multipart;
    }

    public bool CanRetry => !_params.Any(p => p.Value is BinaryContent or IEnumerable<BinaryContent>);

    public static FormRequest Create(IReadOnlyCollection<MultipartParam> @params) => new(@params);

    private static void AddPart(MultipartFormDataContent multipart, MultipartParam param)
    {
        var (name, value, contentType) = param;

        switch (value)
        {
            case null:
                return;
            case BinaryContent file:
                multipart.AddFilePart(name, file);
                return;
            case IEnumerable<BinaryContent> files:
            {
                foreach (var item in files)
                    multipart.AddFilePart(name, item);
                return;
            }
        }

        if (TryGetJsonMediaType(contentType, out var jsonMediaType))
        {
            multipart.AddJsonPart(name, value, jsonMediaType);
            return;
        }

        foreach (var pair in ParameterFlattener.Flatten(new Param(name, value)))
            multipart.AddStringPart(pair.Key, pair.Value);
    }

    private static bool TryGetJsonMediaType(string? contentType,
        [NotNullWhen(true)] out MediaTypeHeaderValue? mediaType)
    {
        mediaType = null;

        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            || parsed.MediaType is not { } mediaTypeName)
            return false;

        var isJson = string.Equals(mediaTypeName, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaTypeName.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        if (!isJson)
            return false;

        parsed.CharSet = null;
        mediaType = parsed;
        return true;
    }
}

file static class MultipartFormDataContentExtensions
{
    extension(MultipartFormDataContent multipart)
    {
        public void AddFilePart(string name, BinaryContent file)
        {
            var fileContent = new StreamContent(new NonDisposingStream(file.Stream));
            fileContent.Headers.ContentType = file.ContentType;
            if (file.FileName is { } fileName)
                multipart.Add(fileContent, name, fileName);
            else
                multipart.Add(fileContent, name);
        }

        public void AddJsonPart(string name, object value, MediaTypeHeaderValue mediaType)
        {
            var json = JsonContent.Create(value, value.GetType());
            json.Headers.ContentType = mediaType;
            multipart.Add(json, name);
        }

        public void AddStringPart(string name, string text)
        {
            var content = new StringContent(text);
            if (content.Headers.ContentType is { } contentType)
                contentType.CharSet = null;
            multipart.Add(content, name);
        }
    }
}
