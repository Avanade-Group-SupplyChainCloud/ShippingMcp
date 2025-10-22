using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Register MCP + HTTP transport + your tools (one line is fine)
builder.Services.AddMcpServer().WithHttpTransport().WithTools<Tools>();

var app = builder.Build();

// Map the MCP JSON-RPC transport at the ROOT "/"
app.MapMcp();

app.Run();

// -------------------- Tools --------------------

public class Tools
{
    private static readonly ConcurrentDictionary<string, object> Store = new();

    private static readonly Dictionary<string, HashSet<string>> CarrierServices = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["UPS"] = new(["Ground", "2Day"], StringComparer.OrdinalIgnoreCase),
        ["FedEx"] = new(["Air", "Overnight"], StringComparer.OrdinalIgnoreCase),
    };

    private static readonly HashSet<string> AllowedLabelSizes = new(
        new[] { "4x6", "6x9" },
        StringComparer.OrdinalIgnoreCase
    );

    // PLAN-AS-A-TOOL (so no [McpResource] needed)
    [McpServerTool, Description("Return ordered steps. Thi is how to setup Ship.")]
    public object get_plan() =>
        new ExecutionPlan
        {
            version = "1.0",
            onFailure = "stop-and-ask",
            steps = new[]
            {
                new ExecutionStep
                {
                    id = "carrier",
                    tool = "carrier_create",
                    inputs = ["carrier", "service"],
                },
                new ExecutionStep
                {
                    id = "label",
                    tool = "label_set",
                    inputs = ["label_size"],
                },
            },
        };

    [McpServerTool, Description("List supported carriers, services, and label sizes.")]
    public object list_supported() =>
        new
        {
            carriers = CarrierServices.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase
            ),
            labelSizes = AllowedLabelSizes
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };

    [McpServerTool, Description("Create a carrier with a service.")]
    public object carrier_create(string carrier, string service)
    {
        if (string.IsNullOrWhiteSpace(carrier))
            return new { ok = false, error = "carrier is required" };
        if (string.IsNullOrWhiteSpace(service))
            return new { ok = false, error = "service is required" };

        if (!CarrierServices.TryGetValue(carrier.Trim(), out var allowed))
            return new { ok = false, error = "unsupported carrier (UPS, FedEx)" };

        if (!allowed.Contains(service.Trim()))
            return new
            {
                ok = false,
                error = $"unsupported service for {carrier} (allowed: {string.Join(", ", allowed)})",
            };

        var saved = new { carrier, service };
        Store["carrier"] = saved;
        return new { ok = true, saved };
    }

    [McpServerTool, Description("Set label size.")]
    public object label_set(string label_size)
    {
        if (!Store.ContainsKey("carrier"))
            return new { ok = false, error = "carrier not created yet; run carrier_create first" };

        if (string.IsNullOrWhiteSpace(label_size))
            return new { ok = false, error = "label_size is required" };

        if (!AllowedLabelSizes.Contains(label_size.Trim()))
            return new { ok = false, error = "unsupported label_size (allowed: 4x6, 6x9)" };

        var saved = new { size = label_size };
        Store["label"] = saved;
        return new { ok = true, saved };
    }
}

// -------------------- Models (for get_plan) --------------------

public record ExecutionPlan
{
    public string version { get; init; } = "1.0";
    public string onFailure { get; init; } = "stop-and-ask";
    public ExecutionStep[] steps { get; init; } = [];
}

public record ExecutionStep
{
    public string id { get; init; } = "";
    public string tool { get; init; } = "";
    public string[] inputs { get; init; } = [];
}