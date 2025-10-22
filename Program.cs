using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer().WithHttpTransport().WithTools<Tools>();
var app = builder.Build();

app.MapGet("/health", () => Results.Ok("Healthy"));

app.MapMcp();
app.Run();

public class Tools
{
    // ---- Simple in-memory store ----
    private static readonly ConcurrentDictionary<string, object> Store = new();

    // Required steps in order
    private static readonly string[] ChecklistOrder = new[]
    {
        "carrier", // from carrier_create
        "service_options", // from set_service_options
        "label", // from label_set
        "printer", // from printer_set
        "notification", // from notification_set
    };

    // Optional: very basic “prod submission pending” flag
    private const string PendingConfirmationKey = "pending_confirmation";

    // Example validation data (can be replaced with your own sources)
    private static readonly Dictionary<string, HashSet<string>> CarrierServices = new()
    {
        ["UPS"] = new(new[] { "Ground", "2Day" }),
        ["FedEx"] = new(new[] { "Air", "Overnight" }),
    };

    // Central registry of option providers per step
    // Each provider returns a plain object with choices. Providers may look at Store if needed.
    private static readonly Dictionary<string, Func<object>> OptionProviders = new()
    {
        ["carrier"] = () =>
            new { carriers = CarrierServices.Keys, servicesByCarrier = CarrierServices },
        ["service_options"] = () =>
            new { insurance = new[] { true, false }, signature = new[] { true, false } },
        ["label"] = () => new { size = new[] { "4x6", "6x9" } },
        ["printer"] = () => new { suggestions = new[] { "Zebra-ZD420", "Zebra-ZT230" } },
        ["notification"] = () => new { format = "email" },
    };

    // ---------- Helpers ----------
    private static object Status()
    {
        var missing = ChecklistOrder.Where(k => !Store.ContainsKey(k)).ToArray();
        var next = missing.FirstOrDefault();
        var ok = missing.Length == 0;

        var snapshot = new Dictionary<string, object>();
        foreach (var key in ChecklistOrder)
        {
            if (Store.TryGetValue(key, out var v))
            {
                snapshot[key] = v!;
            }
        }

        return new
        {
            ok,
            nextStep = next,
            missing,
            summary = snapshot,
        };
    }

    private static object GuardPrereqs(string currentKey)
    {
        var index = Array.IndexOf(ChecklistOrder, currentKey);
        if (index < 0)
        {
            return new { ok = false, error = $"Unknown step '{currentKey}'." };
        }

        var requiredBefore = ChecklistOrder.Take(index).Where(k => !Store.ContainsKey(k)).ToArray();
        if (requiredBefore.Length > 0)
        {
            return new
            {
                ok = false,
                error = $"Step '{currentKey}' requires earlier steps.",
                needed = requiredBefore,
                status = Status(),
            };
        }

        return new { ok = true };
    }

    private static string Json(object o)
    {
        return JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true });
    }

    // ---------- Options tools (generic, no option text in descriptions) ----------

    [McpServerTool, Description("Get available options for a configuration step.")]
    public object get_step_options(string step)
    {
        if (!ChecklistOrder.Contains(step))
        {
            return new { ok = false, error = "Unknown step." };
        }

        if (!OptionProviders.TryGetValue(step, out var provider))
        {
            return new
            {
                ok = true,
                step,
                options = (object?)null,
            };
        }

        var options = provider.Invoke();
        return new
        {
            ok = true,
            step,
            options,
        };
    }

    [McpServerTool, Description("Get options for the next required configuration step.")]
    public object get_next_options()
    {
        var s = (dynamic)Status();
        if (s.ok)
        {
            return new
            {
                ok = true,
                message = "All steps complete.",
                nextStep = (string?)null,
                options = (object?)null,
            };
        }

        var next = (string)s.nextStep;
        var res = get_step_options(next) as dynamic;
        return new
        {
            ok = true,
            nextStep = next,
            options = res.options,
        };
    }

    // ---------- Config tools (generic descriptions, minimal validation text) ----------

    [McpServerTool, Description("Create or update carrier configuration.")]
    public object carrier_create(string carrier, string service)
    {
        // Optional validation (can be replaced/removed)
        if (!CarrierServices.ContainsKey(carrier))
        {
            return new { ok = false, error = "Invalid carrier." };
        }
        if (!CarrierServices[carrier].Contains(service))
        {
            return new { ok = false, error = "Invalid service for carrier." };
        }

        Store["carrier"] = new { carrier, service };
        Store.TryRemove(PendingConfirmationKey, out _);
        return new
        {
            ok = true,
            saved = Store["carrier"],
            status = Status(),
        };
    }

    [McpServerTool, Description("Set service options.")]
    public object set_service_options(bool insurance, bool signature)
    {
        var g = GuardPrereqs("service_options");
        if (!((dynamic)g).ok)
        {
            return g;
        }

        Store["service_options"] = new { insurance, signature };
        Store.TryRemove(PendingConfirmationKey, out _);
        return new
        {
            ok = true,
            saved = Store["service_options"],
            status = Status(),
        };
    }

    [McpServerTool, Description("Set label details.")]
    public object label_set(string label_size)
    {
        var g = GuardPrereqs("label");
        if (!((dynamic)g).ok)
        {
            return g;
        }

        // Optional validation
        if (label_size != "4x6" && label_size != "6x9")
        {
            return new { ok = false, error = "Invalid label size." };
        }

        Store["label"] = new { size = label_size };
        Store.TryRemove(PendingConfirmationKey, out _);
        return new
        {
            ok = true,
            saved = Store["label"],
            status = Status(),
        };
    }

    [McpServerTool, Description("Set printer.")]
    public object printer_set(string printer_name)
    {
        var g = GuardPrereqs("printer");
        if (!((dynamic)g).ok)
        {
            return g;
        }

        Store["printer"] = new { name = printer_name };
        Store.TryRemove(PendingConfirmationKey, out _);
        return new
        {
            ok = true,
            saved = Store["printer"],
            status = Status(),
        };
    }

    [McpServerTool, Description("Set notification destination.")]
    public object notification_set(string email)
    {
        var g = GuardPrereqs("notification");
        if (!((dynamic)g).ok)
        {
            return g;
        }

        Store["notification"] = new { email };
        Store.TryRemove(PendingConfirmationKey, out _);
        return new
        {
            ok = true,
            saved = Store["notification"],
            status = Status(),
        };
    }

    // ---------- Status & finalize/confirm flow ----------

    [McpServerTool, Description("Show setup status and summary.")]
    public object setup_status()
    {
        var s = (dynamic)Status();

        if (s.ok)
        {
            var summary = Json(s.summary);
            return new
            {
                ok = true,
                message = "All required data is set. Review the summary below. Call finalize() to proceed to production confirmation.",
                summary,
            };
        }
        else
        {
            return new
            {
                ok = false,
                message = $"Configuration incomplete. Next step: '{s.nextStep}'.",
                missing = s.missing,
                status = s,
            };
        }
    }

    [McpServerTool, Description("Prompt for production submission confirmation.")]
    public object finalize()
    {
        var s = (dynamic)Status();
        if (!s.ok)
        {
            return new
            {
                ok = false,
                error = "Cannot finalize: configuration incomplete.",
                missing = s.missing,
                nextStep = s.nextStep,
                status = s,
            };
        }

        Store[PendingConfirmationKey] = true;
        return new
        {
            ok = true,
            message = "Are you sure you want to submit this to production? Call confirm_submit(true) to proceed or confirm_submit(false) to cancel.",
            summary = s.summary,
        };
    }

    [McpServerTool, Description("Confirm or cancel production submission.")]
    public object confirm_submit(bool yes)
    {
        if (!Store.TryGetValue(PendingConfirmationKey, out _))
        {
            return new { ok = false, error = "No pending confirmation. Call finalize() first." };
        }

        Store.TryRemove(PendingConfirmationKey, out _);

        if (!yes)
        {
            return new { ok = true, message = "Submission canceled." };
        }

        var s = (dynamic)Status();
        return new
        {
            ok = true,
            message = "Submitted to production.",
            submitted = s.summary,
        };
    }
}