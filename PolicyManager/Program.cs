using System.Reflection;
using System.Text.RegularExpressions;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.Services;

var environmentName = ResolveEnvironmentName(args);

if (IsDevelopmentEnvironment(environmentName))
{
    Env.Load("../.env");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
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

builder.Services.AddScoped<IPolicyHoldersService, PolicyHoldersService>();
builder.Services.AddScoped<IPoliciesService, PoliciesService>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();
builder.Services.AddHostedService<OutboxProcessorBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
