using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Data;
using PolicyManager.Services;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment()) Env.Load("../.env");


builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = $"Server={Environment.GetEnvironmentVariable("DB_HOST")};" +
                       $"Database={Environment.GetEnvironmentVariable("DB_NAME")};" +
                       $"User Id={Environment.GetEnvironmentVariable("DB_USER")};" +
                       $"Password={Environment.GetEnvironmentVariable("DB_PASSWORD")};" +
                       $"TrustServerCertificate=True";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IPolicyHoldersService, PolicyHoldersService>();
builder.Services.AddScoped<IPoliciesService, PoliciesService>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();

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