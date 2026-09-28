using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetEnv;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.Errors;
using PolicyManager.Health;
using PolicyManager.Middleware;
using PolicyManager.Services;

var environmentName = ResolveEnvironmentName(args);

if (IsDevelopmentEnvironment(environmentName))
{
    Env.Load("../.env");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache(o => o.SizeLimit = 10 * 1024 * 1024);
// Every failure leaves as ProblemDetails carrying the correlation ID, whether it is produced by
// model binding, by the exception handler, or by a controller returning ObjectResult.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;

        if (context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId))
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(options =>
    {
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        options.IncludeXmlComments(xmlPath);
    });
}

var connectionString = ResolveConnectionString(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// "ready" marks the check as a readiness dependency rather than a liveness one: the process can be up
// and still not able to serve a request until the database answers. The timeout bounds how long a
// hung connection can hold a probe open, so an orchestrator gets a verdict rather than a hang.
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sql-server", tags: new[] { "ready" }, timeout: TimeSpan.FromSeconds(5));

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));


builder.Services.AddScoped<IPolicyHoldersService, PolicyHoldersService>();
builder.Services.AddScoped<IPoliciesService, PoliciesService>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.TryAddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxProcessorBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();

// Must precede the exception handler so the correlation ID is already in scope when a failure is
// classified, and precede the rest of the pipeline so it covers everything downstream.
app.UseExceptionHandler();

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

// Liveness answers "is this process up", so it reports every registered check: a pod whose database
// is unreachable is running and should be reported as such, not restarted for being unable to reach
// SQL Server, which restarting will not fix. Readiness answers "can this instance serve a request
// now", so it runs only the checks tagged "ready" and is what a load balancer should poll.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = WriteHealthResponseAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});

app.Run();

static string ResolveEnvironmentName(string[] args)
{
    return new ConfigurationBuilder()
        .AddEnvironmentVariables("DOTNET_")
        .AddEnvironmentVariables("ASPNETCORE_")
        .AddCommandLine(args)
        .Build()["environment"]
        ?? Environments.Production;
}

static bool IsDevelopmentEnvironment(string environmentName)
{
    return string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
}

static string ResolveConnectionString(IConfiguration configuration, string environmentName)
{
    const string connectionStringName = "DefaultConnection";
    const string placeholderPattern = @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}";

    var template = configuration.GetConnectionString(connectionStringName);

    if (string.IsNullOrWhiteSpace(template))
        throw new InvalidOperationException(
            $"No connection string named '{connectionStringName}' is configured for the " +
            $"'{environmentName}' environment. Supply it as the " +
            $"ConnectionStrings__{connectionStringName} environment variable or as a " +
            $"ConnectionStrings:{connectionStringName} entry in the matching appsettings file.");

    var unresolvedVariables = new List<string>();

    var resolved = Regex.Replace(template, placeholderPattern, match =>
    {
        var name = match.Groups["name"].Value;
        var value = configuration[name] ?? string.Empty;

        if (value.Length == 0)
        {
            unresolvedVariables.Add(name);
            return match.Value;
        }

        return value;
    });

    if (unresolvedVariables.Count > 0 && !IsDevelopmentEnvironment(environmentName))
        throw new InvalidOperationException(
            $"Connection string '{connectionStringName}' for the '{environmentName}' " +
            $"environment references unresolved environment variable(s): " +
            $"{string.Join(", ", unresolvedVariables)}. Set them for this process or " +
            $"supply a complete connection string as the " +
            $"ConnectionStrings__{connectionStringName} environment variable.");

    return resolved;
}

// Deliberately not HealthCheckResponseWriter.WriteMinimalPlaintext: that writer puts
// entry.Exception.Message in the response, and a health endpoint is unauthenticated. This one emits
// only the status, the description the check chose to publish, and the timings — not the check's
// Data either, which a future check could fill with anything.
static async Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 3),
        checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => new
            {
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 3)
            })
    };

    await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

public partial class Program
{
}
