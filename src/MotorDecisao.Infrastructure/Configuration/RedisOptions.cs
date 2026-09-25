namespace MotorDecisao.Infrastructure.Configuration;

/// <summary>
/// Settings for the Redis-backed published-flow cache. Read from environment
/// variables (the <c>REDIS__</c> prefix maps to this section). When disabled or
/// unreachable, the app falls back to the in-memory cache, so running without a
/// Redis instance keeps working.
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Whether to use Redis for the published-flow cache. Off by default so local
    /// runs without Docker keep using the in-memory cache. Set
    /// <c>REDIS__ENABLED=true</c> to turn it on.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Full StackExchange.Redis connection string. When empty, it is built from
    /// <see cref="Host"/>/<see cref="Port"/>/<see cref="Password"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public string? Password { get; set; }

    /// <summary>Key prefix so the motor's keys are namespaced in a shared Redis.</summary>
    public string KeyPrefix { get; set; } = "motor:published-flow:";

    /// <summary>
    /// Builds the options from environment variables:
    /// <c>REDIS__ENABLED</c>, <c>REDIS__CONNECTIONSTRING</c>,
    /// <c>REDIS__HOST</c>, <c>REDIS__PORT</c>, <c>REDIS__PASSWORD</c>,
    /// <c>REDIS__KEYPREFIX</c>.
    /// </summary>
    public static RedisOptions FromEnvironment()
    {
        var options = new RedisOptions
        {
            Enabled = ParseBool(Environment.GetEnvironmentVariable("REDIS__ENABLED"), defaultValue: false),
            ConnectionString = Environment.GetEnvironmentVariable("REDIS__CONNECTIONSTRING"),
            Host = Environment.GetEnvironmentVariable("REDIS__HOST") ?? "localhost",
            Password = Environment.GetEnvironmentVariable("REDIS__PASSWORD"),
        };

        var portRaw = Environment.GetEnvironmentVariable("REDIS__PORT");
        if (int.TryParse(portRaw, out var port))
        {
            options.Port = port;
        }

        var prefix = Environment.GetEnvironmentVariable("REDIS__KEYPREFIX");
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            options.KeyPrefix = prefix;
        }

        return options;
    }

    /// <summary>
    /// Returns the StackExchange.Redis connection string, preferring an explicit
    /// <see cref="ConnectionString"/> and otherwise composing one. Sets
    /// <c>abortConnect=false</c> so a momentarily-down Redis doesn't throw at
    /// startup (the cache degrades to a miss instead).
    /// </summary>
    public string ResolveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString;
        }

        var auth = string.IsNullOrEmpty(Password) ? string.Empty : $",password={Password}";
        return $"{Host}:{Port}{auth},abortConnect=false";
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes";
    }
}
