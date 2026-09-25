using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MotorDecisao.Api.Tests;

public class FlowApiTests : IClassFixture<MotorApiFactory>
{
    private readonly HttpClient _client;

    public FlowApiTests(MotorApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Full_lifecycle_create_save_publish_decide_audit()
    {
        // 1. Create a flow.
        var createResp = await _client.PostAsJsonAsync("/flows",
            new { name = $"Politica Auto {Guid.NewGuid():N}", description = "teste" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var flow = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var flowId = flow.GetProperty("id").GetGuid();
        var versionId = flow.GetProperty("versions")[0].GetProperty("id").GetGuid();

        // 2. Save a graph: Start -> Condition(idade>=18) -> Approve / Deny.
        var graph = new
        {
            nodes = new object[]
            {
                new { nodeKey = "start", kind = "Start", label = "Início", positionX = 0.0, positionY = 0.0, config = "{}", rulesetKey = (string?)null },
                new { nodeKey = "cond", kind = "Condition", label = "Maior de idade", positionX = 100.0, positionY = 0.0, config = "{\"expression\":\"idade >= 18\"}", rulesetKey = (string?)null },
                new { nodeKey = "approve", kind = "Decision", label = "Aprovar", positionX = 200.0, positionY = -50.0, config = "{\"outcome\":\"Approved\"}", rulesetKey = (string?)null },
                new { nodeKey = "deny", kind = "Decision", label = "Negar", positionX = 200.0, positionY = 50.0, config = "{\"outcome\":\"Denied\"}", rulesetKey = (string?)null }
            },
            edges = new object[]
            {
                new { edgeKey = "e1", sourceNodeKey = "start", targetNodeKey = "cond", sourceHandle = (string?)null, label = (string?)null },
                new { edgeKey = "e2", sourceNodeKey = "cond", targetNodeKey = "approve", sourceHandle = "true", label = (string?)null },
                new { edgeKey = "e3", sourceNodeKey = "cond", targetNodeKey = "deny", sourceHandle = "false", label = (string?)null }
            },
            rulesets = Array.Empty<object>(),
            formulas = Array.Empty<object>()
        };

        var saveResp = await _client.PutAsJsonAsync($"/flows/{flowId}/versions/{versionId}", graph);
        Assert.Equal(HttpStatusCode.OK, saveResp.StatusCode);

        // 3. Publish.
        var publishResp = await _client.PostAsync($"/flows/{flowId}/versions/{versionId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishResp.StatusCode);

        // 4. Decide with idade=25 -> Approved.
        var decideResp = await _client.PostAsJsonAsync($"/flows/{flowId}/decisions",
            new { proposalReference = "PROP-1", fields = new { idade = 25 } });
        Assert.Equal(HttpStatusCode.OK, decideResp.StatusCode);

        var decision = await decideResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", decision.GetProperty("outcome").GetString());
        var executionId = decision.GetProperty("executionId").GetGuid();

        // 5. Audit: fetch the execution with its trace.
        var execResp = await _client.GetAsync($"/executions/{executionId}");
        Assert.Equal(HttpStatusCode.OK, execResp.StatusCode);
        var exec = await execResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", exec.GetProperty("outcome").GetString());
        Assert.True(exec.GetProperty("trace").GetArrayLength() > 0);

        // 6. Decide with idade=16 -> Denied.
        var denyResp = await _client.PostAsJsonAsync($"/flows/{flowId}/decisions",
            new { proposalReference = "PROP-2", fields = new { idade = 16 } });
        var denyDecision = await denyResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Denied", denyDecision.GetProperty("outcome").GetString());

        // 7. Executions list has both.
        var listResp = await _client.GetAsync($"/flows/{flowId}/executions");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, list.GetArrayLength());
    }

    [Fact]
    public async Task Input_fields_round_trip_on_save_and_load()
    {
        var createResp = await _client.PostAsJsonAsync("/flows",
            new { name = $"Campos {Guid.NewGuid():N}", description = (string?)null });
        var flow = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var flowId = flow.GetProperty("id").GetGuid();
        var versionId = flow.GetProperty("versions")[0].GetProperty("id").GetGuid();

        var graph = new
        {
            nodes = new object[]
            {
                new { nodeKey = "start", kind = "Start", label = "Início", positionX = 0.0, positionY = 0.0, config = "{}", rulesetKey = (string?)null }
            },
            edges = Array.Empty<object>(),
            rulesets = Array.Empty<object>(),
            formulas = Array.Empty<object>(),
            inputFields = new object[]
            {
                new { name = "cpf", label = "CPF", type = "Text", required = true, order = 1 },
                new { name = "renda_mensal", label = "Renda mensal", type = "Number", required = false, order = 2 }
            }
        };

        var saveResp = await _client.PutAsJsonAsync($"/flows/{flowId}/versions/{versionId}", graph);
        Assert.Equal(HttpStatusCode.OK, saveResp.StatusCode);

        var loaded = await _client.GetFromJsonAsync<JsonElement>($"/flows/{flowId}/versions/{versionId}");
        var fields = loaded.GetProperty("inputFields");
        Assert.Equal(2, fields.GetArrayLength());
        Assert.Equal("cpf", fields[0].GetProperty("name").GetString());
        Assert.Equal("Text", fields[0].GetProperty("type").GetString());
        Assert.True(fields[0].GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Publish_invalid_flow_returns_400()
    {
        var createResp = await _client.PostAsJsonAsync("/flows",
            new { name = $"Invalido {Guid.NewGuid():N}", description = (string?)null });
        var flow = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var flowId = flow.GetProperty("id").GetGuid();
        var versionId = flow.GetProperty("versions")[0].GetProperty("id").GetGuid();

        // Graph with no Start node -> compilation fails at publish.
        var badGraph = new
        {
            nodes = new object[]
            {
                new { nodeKey = "approve", kind = "Decision", label = "Aprovar", positionX = 0.0, positionY = 0.0, config = "{\"outcome\":\"Approved\"}", rulesetKey = (string?)null }
            },
            edges = Array.Empty<object>(),
            rulesets = Array.Empty<object>(),
            formulas = Array.Empty<object>()
        };
        await _client.PutAsJsonAsync($"/flows/{flowId}/versions/{versionId}", badGraph);

        var publishResp = await _client.PostAsync($"/flows/{flowId}/versions/{versionId}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, publishResp.StatusCode);
    }

    [Fact]
    public async Task Delete_flow_removes_it()
    {
        var createResp = await _client.PostAsJsonAsync("/flows",
            new { name = $"ParaExcluir {Guid.NewGuid():N}", description = (string?)null });
        var flow = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var flowId = flow.GetProperty("id").GetGuid();

        var del = await _client.DeleteAsync($"/flows/{flowId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await _client.GetAsync($"/flows/{flowId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Delete_missing_flow_returns_404()
    {
        var del = await _client.DeleteAsync($"/flows/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
    }

    [Fact]
    public async Task Publish_flow_with_variable_cycle_returns_400()
    {
        var createResp = await _client.PostAsJsonAsync("/flows",
            new { name = $"Ciclo {Guid.NewGuid():N}", description = (string?)null });
        var flow = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var flowId = flow.GetProperty("id").GetGuid();
        var versionId = flow.GetProperty("versions")[0].GetProperty("id").GetGuid();

        var graph = new
        {
            nodes = new object[]
            {
                new { nodeKey = "start", kind = "Start", label = "Início", positionX = 0.0, positionY = 0.0, config = "{}", rulesetKey = (string?)null },
                new { nodeKey = "d", kind = "Decision", label = "Ap", positionX = 0.0, positionY = 120.0, config = "{\"outcome\":\"Approved\"}", rulesetKey = (string?)null }
            },
            edges = new object[]
            {
                new { edgeKey = "e", sourceNodeKey = "start", targetNodeKey = "d", sourceHandle = (string?)null, label = (string?)null }
            },
            rulesets = Array.Empty<object>(),
            formulas = new object[]
            {
                new { key = "a", label = "a", expression = "{b} + 1" },
                new { key = "b", label = "b", expression = "{a} + 1" }
            },
            inputFields = Array.Empty<object>()
        };
        await _client.PutAsJsonAsync($"/flows/{flowId}/versions/{versionId}", graph);

        var publishResp = await _client.PostAsync($"/flows/{flowId}/versions/{versionId}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, publishResp.StatusCode);
        var body = await publishResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Ciclo", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Editing_published_version_is_rejected_with_conflict()
    {
        var createResp = await _client.PostAsJsonAsync("/flows",
            new { name = $"Publicado {Guid.NewGuid():N}", description = (string?)null });
        var flow = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var flowId = flow.GetProperty("id").GetGuid();
        var versionId = flow.GetProperty("versions")[0].GetProperty("id").GetGuid();

        var graph = new
        {
            nodes = new object[]
            {
                new { nodeKey = "start", kind = "Start", label = "Início", positionX = 0.0, positionY = 0.0, config = "{}", rulesetKey = (string?)null },
                new { nodeKey = "approve", kind = "Decision", label = "Aprovar", positionX = 100.0, positionY = 0.0, config = "{\"outcome\":\"Approved\"}", rulesetKey = (string?)null }
            },
            edges = new object[]
            {
                new { edgeKey = "e1", sourceNodeKey = "start", targetNodeKey = "approve", sourceHandle = (string?)null, label = (string?)null }
            },
            rulesets = Array.Empty<object>(),
            formulas = Array.Empty<object>()
        };
        await _client.PutAsJsonAsync($"/flows/{flowId}/versions/{versionId}", graph);
        await _client.PostAsync($"/flows/{flowId}/versions/{versionId}/publish", null);

        // Editing the now-published version must be rejected.
        var editResp = await _client.PutAsJsonAsync($"/flows/{flowId}/versions/{versionId}", graph);
        Assert.Equal(HttpStatusCode.Conflict, editResp.StatusCode);
    }
}
