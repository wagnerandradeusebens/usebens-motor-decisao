using System.Text.Json;
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;

namespace MotorDecisao.Infrastructure.Configuration;

/// <summary>
/// Resolves the effective PostgreSQL connection string, mirroring the credential
/// strategy of the other Usebens services:
/// <list type="bullet">
/// <item>When Secrets Manager is enabled, the RDS-format secret is fetched using
/// the AWS SDK default credential chain (IRSA in EKS) and the connection string
/// is assembled from it.</item>
/// <item>Otherwise the discrete <c>Database</c> settings from <c>.env</c> are used
/// (local development).</item>
/// </list>
/// The schema is intentionally fixed to <see cref="MotorDecisaoDbContext.Schema"/>
/// (<c>usebens_motor_decisao</c>): the migrations are generated against it, so it
/// is not made runtime-variable to avoid a runtime/migration mismatch.
/// </summary>
public static class PostgresConnectionResolver
{
    /// <summary>
    /// Produces the connection string to use. This runs once at startup; the
    /// result is handed to the DbContext registration.
    /// </summary>
    public static string Resolve(
        SecretsManagerOptions secretsOptions,
        DatabaseOptions databaseOptions)
    {
        if (secretsOptions.UseSecretsManager)
        {
            var secret = FetchSecret(secretsOptions);
            return BuildFromSecret(secret);
        }

        // Local development: build from the discrete .env parts.
        return databaseOptions.Resolve();
    }

    /// <summary>
    /// Reads and parses the RDS-format secret. The AWS call is synchronous here on
    /// purpose: it happens exactly once during application startup.
    /// </summary>
    private static PostgresSecret FetchSecret(SecretsManagerOptions options)
    {
        using var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(options.Region));

        var request = new GetSecretValueRequest { SecretId = options.PostgresSecretName };
        var response = client.GetSecretValueAsync(request).GetAwaiter().GetResult();

        if (string.IsNullOrWhiteSpace(response.SecretString))
        {
            throw new InvalidOperationException(
                $"Secret '{options.PostgresSecretName}' has no string value.");
        }

        var secret = JsonSerializer.Deserialize<PostgresSecret>(response.SecretString);
        if (secret is null || string.IsNullOrWhiteSpace(secret.Host))
        {
            throw new InvalidOperationException(
                $"Secret '{options.PostgresSecretName}' is not a valid RDS-format PostgreSQL secret.");
        }

        return secret;
    }

    /// <summary>
    /// Builds a safe Npgsql connection string. Using the builder rather than
    /// string interpolation guarantees special characters in the password are
    /// escaped correctly.
    /// </summary>
    private static string BuildFromSecret(PostgresSecret secret)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = secret.Host,
            Port = secret.Port,
            Database = secret.ResolveDatabase(),
            Username = secret.ResolveUser(),
            Password = secret.Password,
        };

        return builder.ConnectionString;
    }
}
