using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoftEngine.Core.Scenes.Serialization;

/// <summary>
/// Writes a <see cref="Vector3"/> as <c>[x, y, z]</c> rather than as an object with three named
/// members.
///
/// <para>
/// Purely for the reader. A scene file is meant to be opened in an editor and adjusted by hand,
/// and <c>"position": [0, 3, -12]</c> is a line a person can take in at a glance where
/// <c>{"x":0,"y":3,"z":-12}</c> is three times the width for the same three numbers. The reader
/// still accepts the object form, so a file written by any other tool loads anyway.
/// </para>
/// </summary>
public sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            return ReadObject(ref reader);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("expected an array of three numbers, or an object with x, y and z");
        }

        Span<float> values = stackalloc float[3];
        var count = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (count < 3)
            {
                values[count] = reader.GetSingle();
            }

            count++;
        }

        if (count != 3)
        {
            throw new JsonException($"expected three numbers, got {count}");
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector3 ReadObject(ref Utf8JsonReader reader)
    {
        var vector = Vector3.Zero;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            switch (name?.ToLowerInvariant())
            {
                case "x": vector.X = reader.GetSingle(); break;
                case "y": vector.Y = reader.GetSingle(); break;
                case "z": vector.Z = reader.GetSingle(); break;
            }
        }

        return vector;
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) =>
        writer.WriteRawValue(JsonNumberArray.Format([value.X, value.Y, value.Z]));
}

/// <summary>Writes a <see cref="Quaternion"/> as <c>[x, y, z, w]</c>, for the same reason.</summary>
public sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
{
    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("expected an array of four numbers");
        }

        Span<float> values = stackalloc float[4];
        var count = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (count < 4)
            {
                values[count] = reader.GetSingle();
            }

            count++;
        }

        if (count != 4)
        {
            throw new JsonException($"expected four numbers, got {count}");
        }

        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options) =>
        writer.WriteRawValue(JsonNumberArray.Format([value.X, value.Y, value.Z, value.W]));
}

/// <summary>
/// Formats a short run of numbers as one line of JSON.
/// </summary>
/// <remarks>
/// Written as a raw value rather than through <c>WriteStartArray</c> because an indented
/// <see cref="Utf8JsonWriter"/> puts every array element on a line of its own, with no option to
/// say otherwise. A position would come out as five lines holding three numbers — which is worse
/// than the object form these converters exist to replace, not better.
/// </remarks>
internal static class JsonNumberArray
{
    public static string Format(ReadOnlySpan<float> values)
    {
        var text = new System.Text.StringBuilder(values.Length * 10 + 2);

        text.Append('[');

        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            // JSON has no way to write a NaN or an infinity, and a raw value that contained one
            // would produce a file nothing can read back — including this. A coordinate that is
            // not a number is already broken data, so it is written as zero rather than allowed
            // to corrupt the document around it.
            var value = values[i];

            text.Append(float.IsFinite(value)
                ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "0");
        }

        text.Append(']');

        return text.ToString();
    }
}
