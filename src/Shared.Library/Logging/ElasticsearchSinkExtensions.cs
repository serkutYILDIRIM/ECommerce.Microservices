using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.Elasticsearch;
using System.Collections.Specialized;

namespace Shared.Library.Logging;

/// <summary>
/// Extensions for configuring Elasticsearch logging
/// </summary>
public static class ElasticsearchSinkExtensions
{
    /// <summary>
    /// Adds Elasticsearch sink to the Serilog configuration
    /// </summary>
    public static LoggerConfiguration ConfigureElasticsearch(
        this LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion,
        string environment)
    {
        // Get Elasticsearch configuration
        var elasticsearchSection = configuration.GetSection("Elasticsearch");
        if (!elasticsearchSection.Exists())
            return loggerConfiguration;

        var elasticsearchUrl = elasticsearchSection["Url"];
        if (string.IsNullOrEmpty(elasticsearchUrl))
            return loggerConfiguration;

        // Set up Elasticsearch options
        var options = new ElasticsearchSinkOptions(new Uri(elasticsearchUrl))
        {
            // Base index name with service name for better organization
            IndexFormat = $"logs-{serviceName.ToLower()}-{DateTime.UtcNow:yyyy-MM}",

            // Index lifetime management
            AutoRegisterTemplate = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
            NumberOfShards = 2,
            NumberOfReplicas = 1,

            // Performance settings
            BatchAction = ElasticOpType.Create,
            BatchPostingLimit = 50,
            Period = TimeSpan.FromSeconds(2),
            InlineFields = true,

            // Default pipeline and common fields
            ModifyConnectionSettings = x => x.BasicAuthentication(
                elasticsearchSection["Username"] ?? string.Empty,
                elasticsearchSection["Password"] ?? string.Empty),

            // Customize the document
            CustomFormatter = new ElasticsearchCustomFormatter(serviceName, serviceVersion, environment),

            // Fault handling
            FailureCallback = e =>
                Console.Error.WriteLine("Unable to submit event to Elasticsearch: {0}", e.MessageTemplate),
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                               EmitEventFailureHandling.RaiseCallback |
                               EmitEventFailureHandling.WriteToFailureSink
        };

        // Create a literal index name if specified
        var indexName = elasticsearchSection["IndexName"];
        if (!string.IsNullOrEmpty(indexName))
        {
            options.IndexFormat = indexName;
        }

        // Add authorization if configured
        var apiKey = elasticsearchSection["ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            options.ModifyConnectionSettings = x => x.GlobalHeaders(new NameValueCollection
    {
        { "Authorization", $"ApiKey {apiKey}" }
    });
        }


        // Add Elasticsearch sink
        return loggerConfiguration.WriteTo.Elasticsearch(options);
    }
}

/// <summary>
/// Custom formatter for Elasticsearch documents
/// </summary>

public class ElasticsearchCustomFormatter : ITextFormatter
{
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    private readonly string _environment;

    public ElasticsearchCustomFormatter(string serviceName, string serviceVersion, string environment)
    {
        _serviceName = serviceName;
        _serviceVersion = serviceVersion;
        _environment = environment;
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var formattedLog = FormatToDictionary(logEvent, null);
        foreach (var kvp in formattedLog)
        {
            output.WriteLine($"{kvp.Key}: {kvp.Value}");
        }
    }

    public Dictionary<string, object> FormatToDictionary(LogEvent logEvent, IFormatProvider? formatProvider)
    {
        var result = new Dictionary<string, object>
        {
            ["@timestamp"] = logEvent.Timestamp.ToUniversalTime().ToString("o"),
            ["level"] = logEvent.Level.ToString(),
            ["message"] = logEvent.RenderMessage(formatProvider),
            ["service"] = new Dictionary<string, object>
            {
                ["name"] = _serviceName,
                ["version"] = _serviceVersion,
                ["environment"] = _environment
            }
        };

        if (logEvent.Exception != null)
        {
            result["error"] = new Dictionary<string, object>
            {
                ["message"] = logEvent.Exception.Message,
                ["type"] = logEvent.Exception.GetType().FullName,
                ["stack_trace"] = logEvent.Exception.StackTrace,
                ["inner"] = logEvent.Exception.InnerException?.Message
            };
        }

        result["properties"] = logEvent.Properties.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToString());

        foreach (var prop in logEvent.Properties)
        {
            switch (prop.Key)
            {
                case "TraceId":
                case "SpanId":
                case "ParentSpanId":
                case "RequestPath":
                case "RequestMethod":
                case "StatusCode":
                case "CorrelationId":
                case "Category":
                    result[prop.Key] = prop.Value.ToString().Trim('"');
                    break;
            }
        }

        return result;
    }

}

