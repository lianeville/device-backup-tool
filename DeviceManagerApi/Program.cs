using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Cors.Infrastructure;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddConsole();
});
var logger = loggerFactory.CreateLogger("Startup");

try
{
    Env.Load();
    logger.LogInformation(".env file loaded successfully");
}
catch (Exception ex)
{
    logger.LogError($"Failed to load .env file: {ex.Message}");
}

logger.LogInformation("Adding services to the container");
builder.Services.AddControllers();
logger.LogInformation("Controllers added to the service container");

logger.LogInformation("Setting up CORS");
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

logger.LogInformation("CORS setup successfully");

var app = builder.Build();

logger.LogInformation("Configuring HTTP request pipeline");
app.UseCors("AllowSpecificOrigin");
logger.LogInformation("Routing requests to controllers");
app.MapControllers();

try
{
    logger.LogInformation("Starting the application");
    app.Run();
}
catch (Exception ex)
{
    logger.LogError($"Application failed to start: {ex.Message}");
    throw;
}
