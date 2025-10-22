using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer().WithHttpTransport().WithTools<Tools>();
var app = builder.Build();

app.MapGet("/health", () => Results.Ok("Healthy"));

app.MapMcp();
app.Run();

public class Tools
{
    private static readonly ConcurrentDictionary<string, object> Store = new();

    private static readonly Dictionary<string, HashSet<string>> CarrierServices = new()
    {
        ["UPS"] = new(["Ground", "2Day"]),
        ["FedEx"] = new(["Air", "Overnight"]),
    };

    [McpServerTool, Description("Create or update carrier configuration")]
    public object carrier_create(string carrier, string service)
    {
        if (!CarrierServices.ContainsKey(carrier))
            return new
            {
                ok = false,
                error = $"Invalid carrier. Use: {string.Join(", ", CarrierServices.Keys)}",
            };

        if (!CarrierServices[carrier].Contains(service))
            return new
            {
                ok = false,
                error = $"Invalid service for {carrier}. Use: {string.Join(", ", CarrierServices[carrier])}",
            };

        Store["carrier"] = new { carrier, service };
        return new { ok = true, saved = Store["carrier"] };
    }

    [McpServerTool, Description("Set service options for insurance and signature")]
    public object set_service_options(bool insurance, bool signature)
    {
        Store["service_options"] = new { insurance, signature };
        return new { ok = true, saved = Store["service_options"] };
    }

    [McpServerTool, Description("Set label size (4x6 or 6x9)")]
    public object label_set(string label_size)
    {
        if (label_size != "4x6" && label_size != "6x9")
            return new { ok = false, error = "Invalid size. Use: 4x6 or 6x9" };

        Store["label"] = new { size = label_size };
        return new { ok = true, saved = Store["label"] };
    }

    [McpServerTool, Description("Set printer name")]
    public object printer_set(string printer_name)
    {
        Store["printer"] = new { name = printer_name };
        return new { ok = true, saved = Store["printer"] };
    }

    [McpServerTool, Description("Set notification email")]
    public object notification_set(string email)
    {
        Store["notification"] = new { email };
        return new { ok = true, saved = Store["notification"] };
    }
}