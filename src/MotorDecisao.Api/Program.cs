using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MotorDecisao.Api.Endpoints;
using MotorDecisao.Infrastructure;
using MotorDecisao.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Bring in EF Core / PostgreSQL and everything the engine needs to talk to the DB.
builder.Services.AddInfrastructure(builder.Configuration);

// Accept/emit enums as strings (e.g. "Start", "Approved") in request/response
// bodies, matching what the visual editor sends.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Minimal-API OpenAPI document (served at /openapi/v1.json in Development).
builder.Services.AddOpenApi();

// Health checks. The database check is tagged "ready" so it only affects the
// readiness probe; liveness stays a pure "is the process responding?" signal so
// Kubernetes won't restart a pod over a transient database blip.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MotorDecisaoDbContext>(name: "database", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Simple identity endpoint so hitting the root confirms the service is up.
app.MapGet("/", () => Results.Ok(new
{
    service = "usebens-motor-decisao",
    status = "ok"
}));

// Liveness: process is running. Runs no checks (excludes everything).
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

// Readiness: process is running AND the database is reachable ("ready"-tagged).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Domain endpoints.
app.MapFlowEndpoints();
app.MapDecisionEndpoints();
app.MapSourceEndpoints();
app.MapGlobalVariableEndpoints();
app.MapGlobalParameterTableEndpoints();

app.Run();

// Exposed so integration tests (WebApplicationFactory) can reference the entry point.
public partial class Program;
