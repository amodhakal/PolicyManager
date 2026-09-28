using System.Reflection;
using System.Text.RegularExpressions;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.Errors;
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

public partial class Program
{
}
