using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Configure MCP with HTTP transport + auto-discovered tools
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

var app = builder.Build();

// Expose MCP at the root
app.MapMcp();

// (Optional) Add a health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy ✅ MCP Server is running"));

// Run the server
app.Run();

/// <summary>
/// Contains the tool implementations that will be exposed via the MCP server.
/// </summary>
[McpServerToolType]
public class Tools
{
    private static readonly ConcurrentDictionary<string, ShippingConfig> Store = new();
    private const string Key = "current";
    private static readonly string[] AllowedLabelSizes = ["4x6", "6x9"];

    private static readonly Dictionary<string, string[]> CarrierServices = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["UPS"] = ["Ground", "2Day"],
        ["FedEx"] = ["Overnight", "Air"],
    };

    private ShippingConfig GetOrInit() => Store.GetOrAdd(Key, _ => new ShippingConfig());

    private void Save(ShippingConfig cfg) => Store[Key] = cfg;

    [McpServerTool]
    [Description("Create or update the carrier and service.")]
    public object CreateCarrier(string carrier, string service)
    {
        if (!CarrierServices.TryGetValue(carrier, out var services))
            throw new ArgumentException(
                $"Unsupported carrier '{carrier}'. Supported: {string.Join(", ", CarrierServices.Keys)}"
            );

        if (!services.Any(s => string.Equals(s, service, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(
                $"Unsupported service '{service}' for {carrier}. Supported: {string.Join(", ", services)}"
            );

        var cfg = GetOrInit() with { Carrier = carrier, Service = service };
        Save(cfg);
        return new { message = "Carrier and service set.", config = cfg };
    }

    [McpServerTool]
    [Description("Set label size (4x6 or 6x9).")]
    public object CreateLabel(string labelSize)
    {
        var normalized = labelSize?.Trim().ToLowerInvariant();
        if (!AllowedLabelSizes.Contains(normalized ?? ""))
            throw new ArgumentException(
                $"Invalid size '{labelSize}'. Allowed: {string.Join(", ", AllowedLabelSizes)}"
            );

        var cfg = GetOrInit() with { LabelSize = normalized };
        Save(cfg);
        return new { message = "Label size set.", config = cfg };
    }

    [McpServerTool]
    [Description("Set insurance required (true / false).")]
    public object AddInsurance(bool insurance)
    {
        var cfg = GetOrInit() with { InsuranceRequired = insurance };
        Save(cfg);
        return new { message = insurance ? "Insurance: YES" : "Insurance: NO", config = cfg };
    }
}

public record ShippingConfig
{
    public string? Carrier { get; init; }
    public string? Service { get; init; }
    public string? LabelSize { get; init; }
    public bool? InsuranceRequired { get; init; }
}