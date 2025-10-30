using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Configure MCP with HTTP transport + auto-discovered tools
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

var app = builder.Build();

// Map MCP
app.MapMcp();

app.Run();

// Simple tools without persistence
[McpServerToolType]
public class Tools
{
    private static readonly string[] AllowedLabelSizes = ["4x6", "6x9"];

    [McpServerTool]
    [Description("Set carrier and service (no persistence, just echoes).")]
    public object SetCarrier(string carrier, string service)
    {
        Console.WriteLine($"Carrier: {carrier}, Service: {service}");
        return new { message = $"Carrier={carrier}, Service={service}" };
    }

    [McpServerTool]
    [Description("Set label size (4x6 or6x9).")]
    public object SetLabel(string labelSize)
    {
        var normalized = labelSize?.Trim().ToLowerInvariant();
        if (!AllowedLabelSizes.Contains(normalized ?? ""))
            throw new ArgumentException(
                $"Invalid size '{labelSize}'. Allowed: {string.Join(", ", AllowedLabelSizes)}"
            );
        Console.WriteLine($"LabelSize: {normalized}");
        return new { message = $"LabelSize={normalized}" };
    }

    [McpServerTool]
    [Description("Set insurance flag.")]
    public object SetInsurance(bool insurance)
    {
        Console.WriteLine($"Insurance: {(insurance ? "YES" : "NO")}");
        return new { message = insurance ? "Insurance=YES" : "Insurance=NO" };
    }
}