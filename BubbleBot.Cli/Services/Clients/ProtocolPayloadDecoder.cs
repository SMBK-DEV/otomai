using System.Collections;
using System.Reflection;
using Bubble.Shared.Protocol;
using ProtoBuf;

namespace BubbleBot.Cli.Services.Clients;

/// <summary>
/// Decodes a captured application payload with the generated protocol factory.
/// Output is bounded and omits free-form strings and likely identity/chat fields.
/// </summary>
public static class ProtocolPayloadDecoder
{
    private const int MaxFields = 128;
    private const int MaxDepth = 4;
    private const int MaxCollectionItems = 12;

    private static readonly string[] RedactedFieldFragments =
    [
        "account", "token", "password", "credential", "email", "session",
        "auth", "secret", "nickname", "charactername", "playername",
        "chat", "message", "text", "username", "displayname"
    ];

    public static ProtocolPayloadDecodeResult Decode(string typeUrl, byte[] payload)
    {
        if (string.IsNullOrWhiteSpace(typeUrl))
            return new(false, "Missing protocol type URL.", []);

        if (payload is null || payload.Length == 0)
            return new(false, "Payload is empty.", []);

        if (payload.Length > 4096)
            return new(false, "Payload exceeds the 4096-byte capture limit.", []);

        try
        {
            var type = typeUrl.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? typeUrl;
            type = type.TrimStart('.');
            var message = MessageFactory.Create(type, payload);
            if (message is null)
                return new(false, $"No generated message mapping for '{type}'.", []);

            var fields = new List<ProtocolDecodedField>();
            AppendFields(message, string.Empty, 0, fields);
            return new(true, $"Decoded with generated protocol type {message.GetType().FullName}.", fields);
        }
        catch (Exception ex)
        {
            return new(false, $"Generated protobuf decode failed: {ex.GetType().Name}: {ex.Message}", []);
        }
    }

    private static void AppendFields(object value, string prefix, int depth, List<ProtocolDecodedField> output)
    {
        if (depth > MaxDepth || output.Count >= MaxFields)
            return;

        var properties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ProtoMemberAttribute>()))
            .Where(item => item.Attribute is not null && item.Property.CanRead && item.Property.GetIndexParameters().Length == 0)
            .OrderBy(item => item.Attribute!.Tag);

        foreach (var item in properties)
        {
            if (output.Count >= MaxFields)
                break;

            var property = item.Property;
            var attribute = item.Attribute!;
            var fieldName = string.IsNullOrWhiteSpace(attribute.Name) ? property.Name : attribute.Name;
            var path = string.IsNullOrEmpty(prefix) ? fieldName : $"{prefix}.{fieldName}";

            if (RedactedFieldFragments.Any(fragment => fieldName.Replace("_", "", StringComparison.Ordinal)
                    .Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                output.Add(new ProtocolDecodedField(attribute.Tag, path, "redacted", "[sensitive field omitted]"));
                continue;
            }

            object? fieldValue;
            try { fieldValue = property.GetValue(value); }
            catch { continue; }

            if (fieldValue is null)
                continue;

            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type.IsEnum)
            {
                output.Add(new ProtocolDecodedField(attribute.Tag, path, "enum",
                    $"{fieldValue} ({Convert.ToInt64(fieldValue)})"));
                continue;
            }

            if (fieldValue is string text)
            {
                output.Add(new ProtocolDecodedField(attribute.Tag, path, "string",
                    $"[string value omitted; {text.Length} chars]"));
                continue;
            }

            if (fieldValue is byte[] bytes)
            {
                output.Add(new ProtocolDecodedField(attribute.Tag, path, "bytes",
                    $"[{bytes.Length} bytes omitted]"));
                continue;
            }

            if (fieldValue is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                output.Add(new ProtocolDecodedField(attribute.Tag, path, type.Name, Convert.ToString(fieldValue,
                    System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
                continue;
            }

            if (fieldValue is IDictionary dictionary)
            {
                var index = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (index >= MaxCollectionItems || output.Count >= MaxFields)
                        break;

                    var key = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? index.ToString();
                    var childPath = $"{path}[{key}]";
                    var childValue = entry.Value;
                    if (childValue is null)
                    {
                        output.Add(new ProtocolDecodedField(attribute.Tag, childPath, "null", "(null)"));
                    }
                    else if (childValue.GetType().IsEnum || childValue is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double)
                    {
                        output.Add(new ProtocolDecodedField(attribute.Tag, childPath, childValue.GetType().Name,
                            Convert.ToString(childValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
                    }
                    else if (childValue is string childText)
                    {
                        output.Add(new ProtocolDecodedField(attribute.Tag, childPath, "string",
                            $"[string value omitted; {childText.Length} chars]"));
                    }
                    else
                    {
                        AppendFields(childValue, childPath, depth + 1, output);
                    }
                    index++;
                }
                continue;
            }

            if (fieldValue is IEnumerable sequence)
            {
                var index = 0;
                foreach (var child in sequence)
                {
                    if (index >= MaxCollectionItems || output.Count >= MaxFields)
                        break;
                    if (child is null) { index++; continue; }
                    var childPath = $"{path}[{index}]";
                    if (child.GetType().IsEnum || child is bool or byte or sbyte or short or ushort or int or uint or long or ulong)
                    {
                        output.Add(new ProtocolDecodedField(attribute.Tag, childPath,
                            child.GetType().Name, child.ToString() ?? string.Empty));
                    }
                    else if (child is string childText)
                    {
                        output.Add(new ProtocolDecodedField(attribute.Tag, childPath, "string",
                            $"[string value omitted; {childText.Length} chars]"));
                    }
                    else
                    {
                        AppendFields(child, childPath, depth + 1, output);
                    }
                    index++;
                }
                continue;
            }

            if (type.IsClass || type.IsValueType)
                AppendFields(fieldValue, path, depth + 1, output);
        }
    }
}

public sealed record ProtocolDecodedField(int Number, string Path, string Kind, string Value);
public sealed record ProtocolPayloadDecodeResult(bool Supported, string Message, IReadOnlyList<ProtocolDecodedField> Fields);
