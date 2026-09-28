using Asp.Versioning;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetEnv;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.Errors;
using PolicyManager.Health;
using PolicyManager.Middleware;
using PolicyManager.Messaging;
using PolicyManager.Models;
using PolicyManager.Resilience;
using PolicyManager.Services;
using PolicyManager.Telemetry;

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

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddBearerAuthentication();
builder.Services.AddPolicyAuthorization();

builder.Services.Configure<PiiOptions>(builder.Configuration.GetSection(PiiOptions.SectionName));
builder.Services.AddSingleton<PiiCipher>();
builder.Services.AddSingleton<PiiBackfillService>();

// Versioning is configured before MVC so the API explorer can read it, and both are additive: the
// unversioned routes still exist and still mean 1.0. ReportApiVersions makes every response say which
// versions the endpoint supports, so a client can discover that without a second call or a document.
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(versions =>
    {
        versions.GroupNameFormat = "'v'VVV";
        versions.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(options =>
    {
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        options.IncludeXmlComments(xmlPath);

        // One document per version rather than a merged one, so a diff between two of them is
        // exactly the surface change in that version and nothing else.
        options.SwaggerDoc(ApiVersions.V1, new OpenApiInfo
        {
            Title = "Policy Manager API",
            Version = ApiVersions.V1
        });
    });
}

var connectionString = ResolveConnectionString(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddResiliencePipelines(builder.Configuration);
builder.Services.AddPolicyManagerTelemetry(builder.Configuration, builder.Environment);

// Deliberately no EnableRetryOnFailure here. EF's retrying execution strategy refuses
// user-initiated transactions, and the outbox write path in Data/OutboxTransaction.cs opens one —
// turning it on would turn every create and update into
// "The configured execution strategy does not support user-initiated transactions".
// Database retry therefore lives where it can be applied safely: the readiness probe, and any
// future call that is a single statement outside a unit of work. See the README.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// "ready" marks the check as a readiness dependency rather than a liveness one: the process can be up
// and still not able to serve a request until the database answers. The timeout bounds how long a
// hung connection can hold a probe open, so an orchestrator gets a verdict rather than a hang.
//
// Registered through a HealthCheckRegistration rather than AddCheck<T> because the wrapper decorates
// another check, and a container holding two IHealthCheck registrations cannot tell which one the
// decorator should wrap. Naming the factory makes that explicit at the composition root.
builder.Services.AddHealthChecks()
    .Add(new HealthCheckRegistration(
        "sql-server",
        (IServiceProvider sp) => new ResilientSqlServerHealthCheck(
            ActivatorUtilities.CreateInstance<SqlServerHealthCheck>(sp, []),
            sp.GetRequiredService<ResiliencePipelineFor<DatabasePipeline>>(),
            sp.GetRequiredService<ILogger<ResilientSqlServerHealthCheck>>()),
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" },
        timeout: TimeSpan.FromSeconds(5)));

// Outbound calls go through this client so the resilience pipeline is attached once, at the
// composition root, rather than being re-derived at each call site.
builder.Services.AddTransient<ResilientHttpMessageHandler>();
builder.Services
    .AddHttpClient(HttpClients.Outbound, client => client.Timeout = TimeSpan.FromSeconds(30))
    .AddHttpMessageHandler<ResilientHttpMessageHandler>();

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

// The Kestrel limit is deployment configuration and is read here, eagerly, because Kestrel options
// are frozen once the host is built. The runtime limits are not: they are read per request, so a
// configuration source added after this point still applies.
var rateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
                 ?? new RateLimitOptions();

// The server-side body limit. This is the one that matters: Kestrel refuses to read the rest of an
// oversized upload, so the bytes never accumulate. RequestSizeLimitMiddleware sits in front of it to
// turn the same condition into a ProblemDetails response and to make the behaviour observable under
// TestServer.
if (rateLimits.MaxRequestBodySizeBytes > 0)
{
    builder.WebHost.ConfigureKestrel(kestrel =>
        kestrel.Limits.MaxRequestBodySize = rateLimits.MaxRequestBodySizeBytes);
}

builder.Services.AddApiRateLimiting();


// The write generation is process-wide state, not per request: it has to be shared by the reader
// and the writer that race each other, and those are always different requests. Scoping it would
// give every request its own generation, so every key a request built would be its own.
builder.Services.AddSingleton<PolicyHolderWriteGenerations>();
builder.Services.AddScoped<IPolicyHoldersService, PolicyHoldersService>();
builder.Services.AddScoped<IPoliciesService, PoliciesService>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();
builder.Services.AddScoped<IReportsService, ReportsService>();

// Scoped, not singleton: the generator allocates through the request's own DbContext, so that a
// failure rolls back with the rest of the unit of work rather than against a long-lived connection.
builder.Services.AddScoped<IBusinessNumberGenerator, BusinessNumberGenerator>();

// The audit columns are stamped from here rather than by each service, so a new write path is
// audited by default. ICurrentUser is scoped because it reads the ambient HTTP context, which
// differs per request.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<IPiiGuard, PiiGuard>();

builder.Services.TryAddSingleton(TimeProvider.System);

// TryAdd first so the logging stand-in is what is registered when no broker is configured, and so a
// test host that substitutes its own publisher keeps it. The real transport is registered after, and
// only when Broker:Enabled says so — see BrokerRegistration.
builder.Services.TryAddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
builder.Services.AddOutboxBroker(builder.Configuration);
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxProcessorBackgroundService>();

var app = builder.Build();

// Fails fast at startup rather than at the first request. A deployment that has not supplied a
// signing key must not come up and quietly accept unsigned tokens, and must not come up with
// authentication switched off and looking healthy while doing so. Resolved from the built host so
// the check sees the finished configuration, including any source registered after this point.
app.Services.GetRequiredService<IOptions<JwtOptions>>().Value.Validate();

// The PII cipher is validated first and then published to the static the EF value converters read,
// because a converter is constructed while the model is being built — before any service provider
// exists to resolve options from. Validating before publishing means a missing or short key stops
// the process rather than producing a model that encrypts with nothing.
app.Services.GetRequiredService<IOptions<PiiOptions>>().Value.Validate();
PiiCipher.Use(app.Services.GetRequiredService<PiiCipher>());

// Only on a database that still holds addresses written before encryption existed. Selects rows
// with no blind index, so a converted table makes the first pass return nothing and the task stops.
if (app.Configuration.GetValue($"{PiiOptions.SectionName}:BackfillOnStartup", true))
{
    var backfill = app.Services.GetRequiredService<PiiBackfillService>();
    _ = Task.Run(() => backfill.RunUntilCompleteAsync(CancellationToken.None), CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();

// Ahead of everything that can fail, and ahead of the exception handler's own write path, so an
// oversized body is refused before any model binding or handler work begins.
app.UseMiddleware<RequestSizeLimitMiddleware>();

// Must precede the exception handler so the correlation ID is already in scope when a failure is
// classified, and precede the rest of the pipeline so it covers everything downstream.
app.UseExceptionHandler();

app.UseHttpsRedirection();

// Explicit rather than left to the implicit insertion WebApplication performs at the head of the
// pipeline, so the position of routing relative to the middleware below is stated rather than
// inferred.
app.UseRouting();

// After UseRouting, so a per-endpoint [EnableRateLimiting]/[DisableRateLimiting] override is
// honoured, and after CorrelationIdMiddleware, because the partition key is read from the item it
// stores. Ahead of authentication so a flood is shed before any token is validated — a JWT is a
// signed blob that costs real CPU to check, and verifying one per rejected request would let an
// unauthenticated caller spend the very CPU the limiter exists to protect.
app.UseRateLimiter();

// Ahead of UseAuthorization, which is what actually evaluates the policies, and behind the rate
// limiter so a flood is shed before a token is validated.
app.UseAuthentication();
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
