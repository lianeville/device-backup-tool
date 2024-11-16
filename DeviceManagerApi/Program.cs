using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add logging configuration
builder.Logging.ClearProviders(); // Clears default loggers (e.g., Console, Debug)
builder.Logging.AddConsole(); // Adds console logger

// Create the logger instance using the builder's LoggerFactory
var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddConsole(); // Ensure console logging is enabled
});
var logger = loggerFactory.CreateLogger("Startup");

logger.LogInformation("Loading environment variables from .env file");
try
{
    // Load environment variables from the .env file
    Env.Load();
    logger.LogInformation(".env file loaded successfully");
}
catch (Exception ex)
{
    logger.LogError($"Failed to load .env file: {ex.Message}");
}

// Add services to the container
logger.LogInformation("Adding services to the container");

builder.Services.AddControllers();
logger.LogInformation("Controllers added to the service container");

// Add JWT Authentication
logger.LogInformation("Setting up JWT authentication");
try
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER"),
            ValidAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT_SECRET_KEY") ?? string.Empty)) // Handling possible null value for secret key
        };
    });

    logger.LogInformation("JWT authentication setup successfully");
}
catch (Exception ex)
{
    logger.LogError($"Error during JWT authentication setup: {ex.Message}");
}

// Build the app
var app = builder.Build();

// Configure the HTTP request pipeline
logger.LogInformation("Configuring HTTP request pipeline");

app.UseAuthentication();
app.UseAuthorization();

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
