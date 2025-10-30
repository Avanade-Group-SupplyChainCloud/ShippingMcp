using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

// Build the host for the MCP server
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);

// Configure the MCP server with stdio transport and custom tools
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly(); // Automatically discovers all classes marked with [McpServerToolType]

// Run the server
await builder.Build().RunAsync();

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

    /// <summary>
    /// Creates or updates the shipping carrier and service.
    /// </summary>
    /// <param name="carrier">The carrier name (e.g., UPS, FedEx)</param>
    /// <param name="service">The service type (e.g., Ground, 2Day, Overnight, Air)</param>
    /// <returns>A confirmation message and the updated config</returns>
    [McpServerTool]
    [Description(
        "Create or update the carrier and service. Carrier: UPS/FedEx. Service: Ground/2Day/Overnight/Air."
    )]
    public object CreateCarrier(
        [Description("Carrier name (UPS or FedEx)")] string carrier,
        [Description("Service (Ground,2Day, Overnight, Air)")] string service
    )
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

    /// <summary>
    /// Creates or updates the label size for shipping.
    /// </summary>
    /// <param name="labelSize">The label size (4x6 or 6x9)</param>
    /// <returns>A confirmation message and the updated config</returns>
    [McpServerTool]
    [Description("Create or update the label size. Allowed values:4x6 or6x9.")]
    public object CreateLabel([Description("Label size (4x6 or6x9)")] string labelSize)
    {
        var normalized = labelSize?.Trim().ToLowerInvariant();
        if (!AllowedLabelSizes.Contains(normalized ?? ""))
            throw new ArgumentException(
                $"Invalid labelSize '{labelSize}'. Allowed: {string.Join(", ", AllowedLabelSizes)}"
            );

        var cfg = GetOrInit() with { LabelSize = normalized };
        Save(cfg);
        return new { message = "Label size set.", config = cfg };
    }

    /// <summary>
    /// Adds or updates the insurance requirement for shipping.
    /// </summary>
    /// <param name="insurance">true to require insurance, false otherwise</param>
    /// <returns>A confirmation message and the updated config</returns>
    [McpServerTool]
    [Description("Add or update insurance requirement. true = yes, false = no.")]
    public object AddInsurance([Description("Insurance required (true/false)")] bool insurance)
    {
        var cfg = GetOrInit() with { InsuranceRequired = insurance };
        Save(cfg);
        return new
        {
            message = insurance
                ? "Insurance required set to YES."
                : "Insurance required set to NO.",
            config = cfg,
        };
    }
}

public record ShippingConfig
{
    public string? Carrier { get; init; }
    public string? Service { get; init; }
    public string? LabelSize { get; init; } // "4x6" or "6x9"
    public bool? InsuranceRequired { get; init; }
}