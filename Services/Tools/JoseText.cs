using System;
using System.Linq;
using System.Text.Json;

namespace NotepadAvalonia.Services.Tools;

internal static class JoseText
{
    internal static string DecodeJwt(string input, string newline)
    {
        var parts = Split(input, 3);
        using var header = ParseObject(TextCodecs.DecodeBase64(parts[0], Base64Format.Url));
        using var payload = ParseObject(TextCodecs.DecodeBase64(parts[1], Base64Format.Url));
        if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(alg.GetString()) || ((alg.GetString() == "none") != (parts[2].Length == 0)))
            throw new FormatException("The JWT algorithm and signature fields are inconsistent.");

        return StructuredText.WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("header");
            header.RootElement.WriteTo(writer);
            writer.WritePropertyName("payload");
            payload.RootElement.WriteTo(writer);
            writer.WriteString("signature", parts[2]);
            writer.WriteEndObject();
        }, true, newline);
    }

    internal static string EncodeUnsignedJwt(string input)
    {
        using var payload = ParseObject(input);
        string claims = StructuredText.WriteJson(payload.RootElement.WriteTo, false, "\n");
        return TextCodecs.EncodeBase64("{\"alg\":\"none\",\"typ\":\"JWT\"}", Base64Format.Url) + "." +
            TextCodecs.EncodeBase64(claims, Base64Format.Url) + ".";
    }

    private static readonly string[] JweFields = ["protected", "encryptedKey", "iv", "ciphertext", "tag"];

    internal static string InspectJwe(string input, string newline)
    {
        var parts = Split(input, JweFields.Length);
        using var header = ParseObject(TextCodecs.DecodeBase64(parts[0], Base64Format.Url));
        return StructuredText.WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("protectedHeader");
            header.RootElement.WriteTo(writer);
            for (int i = 0; i < parts.Length; i++) writer.WriteString(JweFields[i], parts[i]);
            writer.WriteEndObject();
        }, true, newline);
    }

    internal static string AssembleJwe(string input)
    {
        using var json = ParseObject(input);
        var parts = JweFields.Select(name => json.RootElement.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new FormatException($"Expected the '{name}' string.")).ToArray();
        foreach (string part in parts) _ = TextCodecs.DecodeBase64Bytes(part, Base64Format.Url);
        using var header = ParseObject(TextCodecs.DecodeBase64(parts[0], Base64Format.Url));
        if (json.RootElement.TryGetProperty("protectedHeader", out var edited) && !JsonElement.DeepEquals(edited, header.RootElement))
            throw new FormatException("The decoded header differs from the original protected bytes.");
        return string.Join('.', parts);
    }

    internal static string FormatJwk(string input, bool indent, string newline)
    {
        using var json = ParseObject(input);
        var root = json.RootElement;
        if (!IsKey(root) && !(root.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array &&
            keys.EnumerateArray().All(IsKey)))
            throw new FormatException("Expected a JWK with a 'kty' field or a JWK Set with a 'keys' array.");
        return StructuredText.WriteJson(root.WriteTo, indent, newline);
    }

    private static bool IsKey(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("kty", out var type) && type.ValueKind == JsonValueKind.String &&
        !string.IsNullOrEmpty(type.GetString());

    private static string[] Split(string input, int count)
    {
        var parts = input.Trim().Split('.');
        if (parts.Length != count) throw new FormatException($"Expected {count} dot-separated token parts.");
        foreach (var part in parts) _ = TextCodecs.DecodeBase64Bytes(part, Base64Format.Url);
        return parts;
    }

    private static JsonDocument ParseObject(string input)
    {
        var json = StructuredText.ParseJson(input);
        if (json.RootElement.ValueKind == JsonValueKind.Object) return json;
        json.Dispose();
        throw new FormatException("Expected a JSON object.");
    }
}
