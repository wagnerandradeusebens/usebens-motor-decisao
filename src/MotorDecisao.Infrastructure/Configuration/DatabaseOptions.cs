namespace MotorDecisao.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed database settings. In local development these come from
/// environment variables / .env; in production the connection string is expected
/// to be assembled from AWS Secrets Manager (same pattern as the other services),
/// which can be layered on top of this without changing consumers.
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Full Npgsql connection string. When empty, it is built from the discrete
    /// <c>Host</c>/<c>Port</c>/<c>Name</c>/<c>User</c>/<c>Password</c> values.
    /// </summary>
    public string? ConnectionString { get; set; }

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = "postgres";
    public string User { get; set; } = "postgres";
    public string Password { get; set; } = "postgres";

    /// <summary>
    /// Returns the connection string to use, preferring an explicit
    /// <see cref="ConnectionString"/> and otherwise composing one from the parts.
    /// </summary>
    public string Resolve()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString;
        }

        return $"Host={Host};Port={Port};Database={Name};Username={User};Password={Password}";
    }
}
