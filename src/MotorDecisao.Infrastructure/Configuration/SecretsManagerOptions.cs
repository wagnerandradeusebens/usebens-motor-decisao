namespace MotorDecisao.Infrastructure.Configuration;

/// <summary>
/// Controls how database credentials are resolved, mirroring the pattern used by
/// the other Usebens services (e.g. usebens-okr):
/// <list type="bullet">
/// <item>Production (EKS): <see cref="UseSecretsManager"/> is on and credentials
/// are read from AWS Secrets Manager via the default credential chain (IRSA).</item>
/// <item>Local development: <see cref="UseSecretsManager"/> is off and credentials
/// come from the <c>Database</c> section / <c>.env</c>.</item>
/// </list>
/// Values are read from environment variables so the same names carry across the
/// stack.
/// </summary>
public class SecretsManagerOptions
{
    /// <summary>
    /// Whether to pull credentials from AWS Secrets Manager. Defaults to
    /// <c>true</c> (production) to match the other services; set
    /// <c>USE_SECRETS_MANAGER=false</c> locally.
    /// </summary>
    public bool UseSecretsManager { get; set; } = true;

    /// <summary>
    /// Name of the RDS-format secret holding the PostgreSQL credentials. Defaults
    /// to the Usebens naming convention for this service.
    /// </summary>
    public string PostgresSecretName { get; set; } = "databases/postgres-motor-decisao";

    /// <summary>AWS region where the secret lives.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Optional schema override. When the secret itself does not carry a
    /// <c>schema</c> field, this (or the compiled-in default) is used.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Builds the options from environment variables, matching the OKR keys:
    /// <c>USE_SECRETS_MANAGER</c>, <c>POSTGRES_SECRET_NAME</c>,
    /// <c>AWS_DEFAULT_REGION</c>, <c>DB_SCHEMA</c>.
    /// </summary>
    public static SecretsManagerOptions FromEnvironment()
    {
        return new SecretsManagerOptions
        {
            UseSecretsManager = ParseBool(Environment.GetEnvironmentVariable("USE_SECRETS_MANAGER"), defaultValue: true),
            PostgresSecretName = Environment.GetEnvironmentVariable("POSTGRES_SECRET_NAME") ?? "databases/postgres-motor-decisao",
            Region = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1",
            Schema = Environment.GetEnvironmentVariable("DB_SCHEMA"),
        };
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
