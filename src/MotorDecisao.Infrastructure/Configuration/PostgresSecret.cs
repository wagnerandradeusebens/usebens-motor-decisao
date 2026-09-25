using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorDecisao.Infrastructure.Configuration;

/// <summary>
/// Shape of the RDS-format secret stored in AWS Secrets Manager, matching what
/// the other Usebens services consume. Field aliases mirror the OKR resolver,
/// which accepts <c>username</c>/<c>login</c> and <c>dbname</c>/<c>database</c>.
/// </summary>
public class PostgresSecret
{
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    /// <summary>
    /// Port. RDS-format secrets may store this as a JSON number or a string, so a
    /// tolerant converter is used.
    /// </summary>
    [JsonPropertyName("port")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int Port { get; set; } = 5432;

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Alternative key for the user, as tolerated by the OKR resolver.</summary>
    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("dbname")]
    public string? DbName { get; set; }

    /// <summary>Alternative key for the database name.</summary>
    [JsonPropertyName("database")]
    public string? Database { get; set; }

    /// <summary>Optional schema carried directly in the secret.</summary>
    [JsonPropertyName("schema")]
    public string? Schema { get; set; }

    /// <summary>Effective user, preferring <c>username</c> then <c>login</c>.</summary>
    public string ResolveUser() => Username ?? Login ?? string.Empty;

    /// <summary>Effective database, preferring <c>dbname</c> then <c>database</c>.</summary>
    public string ResolveDatabase() => DbName ?? Database ?? string.Empty;
}

/// <summary>
/// Reads an integer that may be encoded as a JSON number or a JSON string,
/// tolerating both because RDS-format secrets are not consistent about it.
/// </summary>
public sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetInt32();
            case JsonTokenType.String:
                var s = reader.GetString();
                return int.TryParse(s, out var value)
                    ? value
                    : throw new JsonException($"Valor de porta inválido: '{s}'.");
            default:
                throw new JsonException($"Tipo inesperado para porta: {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
