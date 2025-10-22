using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer().WithHttpTransport().WithTools<Tools>();
var app = builder.Build();
app.MapMcp();
app.Run();

public class Tools
{
    private static readonly ConcurrentDictionary<string, object> Store = new();

    // Define your setup steps here - easily scale to 10+ steps
    private static readonly List<SetupStep> Steps = new()
    {
        new("carrier", "carrier_create", new[] { "carrier", "service" }),
        new("service_options", "set_service_options", new[] { "insurance", "signature" }),
        new("label", "label_set", new[] { "label_size" }),
        new("printer", "printer_set", new[] { "printer_name" }),
        new("notification", "notification_set", new[] { "email" }),
    };

    private static readonly Dictionary<string, HashSet<string>> CarrierServices = new()
    {
        ["UPS"] = new(["Ground", "2Day"]),
        ["FedEx"] = new(["Air", "Overnight"]),
    };

    // -------------------- Core Tools --------------------

    [McpServerTool, Description("Get ordered setup steps")]
    public object plan() =>
        new
        {
            steps = Steps
                .Select(
                    (s, i) =>
                        new
                        {
                            order = i + 1,
                            s.id,
                            s.tool,
                            s.inputs,
                        }
                )
                .ToArray(),
        };

    [McpServerTool, Description("Get current progress")]
    public object status()
    {
        var completed = Steps.Where(s => Store.ContainsKey(s.id)).Select(s => s.id).ToArray();
        var next = Steps.FirstOrDefault(s => !Store.ContainsKey(s.id));

        return new
        {
            done = completed,
            next = next?.tool,
            ready = next == null,
        };
    }

    [McpServerTool, Description("Preview config before saving")]
    public object preview()
    {
        var config = Steps.ToDictionary(
            s => s.id,
            s => Store.ContainsKey(s.id) ? Store[s.id] : null
        );
        var missing = Steps.Where(s => !Store.ContainsKey(s.id)).Select(s => s.id).ToArray();

        return new
        {
            config,
            missing,
            valid = missing.Length == 0,
        };
    }

    [McpServerTool, Description("Save all config")]
    public object commit()
    {
        var prev = preview();
        var validProp = prev.GetType().GetProperty("valid")?.GetValue(prev);

        if (validProp is false)
            return new
            {
                ok = false,
                error = "Incomplete",
                details = prev,
            };

        Store["committed"] = DateTime.UtcNow;
        return new { ok = true, saved = prev };
    }

    // -------------------- Step Tools --------------------

    [McpServerTool, Description("Step 1: Create carrier")]
    public object carrier_create(string carrier, string service)
    {
        if (!CarrierServices.ContainsKey(carrier))
            return new
            {
                ok = false,
                error = $"Invalid carrier. Use: {string.Join(", ", CarrierServices.Keys)}",
            };

        if (!CarrierServices[carrier].Contains(service))
            return new { ok = false, error = $"Invalid service for {carrier}" };

        Store["carrier"] = new { carrier, service };
        return new { ok = true, next = Steps[1].tool };
    }

    [McpServerTool, Description("Step 2: Set service options")]
    public object set_service_options(bool insurance, bool signature)
    {
        if (!Store.ContainsKey("carrier"))
            return new { ok = false, error = "Run carrier_create first" };

        Store["service_options"] = new { insurance, signature };
        return new { ok = true, next = Steps[2].tool };
    }

    [McpServerTool, Description("Step 3: Set label size")]
    public object label_set(string label_size)
    {
        if (!Store.ContainsKey("service_options"))
            return new { ok = false, error = "Run set_service_options first" };

        if (label_size != "4x6" && label_size != "6x9")
            return new { ok = false, error = "Use: 4x6 or 6x9" };

        Store["label"] = new { size = label_size };
        return new { ok = true, next = Steps[3].tool };
    }

    [McpServerTool, Description("Step 4: Set printer")]
    public object printer_set(string printer_name)
    {
        if (!Store.ContainsKey("label"))
            return new { ok = false, error = "Run label_set first" };

        Store["printer"] = new { name = printer_name };
        return new { ok = true, next = Steps[4].tool };
    }

    [McpServerTool, Description("Step 5: Set notification email")]
    public object notification_set(string email)
    {
        if (!Store.ContainsKey("printer"))
            return new { ok = false, error = "Run printer_set first" };

        Store["notification"] = new { email };
        return new { ok = true, next = "commit" };
    }

    [McpServerTool, Description("Reset everything")]
    public object reset()
    {
        Store.Clear();
        return new { ok = true };
    }
}

// -------------------- Model --------------------
public record SetupStep(string id, string tool, string[] inputs);