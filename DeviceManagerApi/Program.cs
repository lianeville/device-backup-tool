using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Cors.Infrastructure;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Clear default logging providers and add Console logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Create logger instance using the builder's LoggerFactory
var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddConsole();
});
var logger = loggerFactory.CreateLogger("Startup");

try
{
    // Load environment variables from .env file
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

// Enable CORS
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

// Set up JWT authentication
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
        var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
        var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
        var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");

        if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience) || string.IsNullOrEmpty(secretKey))
        {
            throw new InvalidOperationException("JWT configuration is incomplete. Please check your environment variables.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)) // Handling the secret key
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

// Order of middleware is important
app.UseCors("AllowSpecificOrigin"); // Apply CORS policy here, before authentication and routing
app.UseAuthentication();
app.UseAuthorization();

logger.LogInformation("Routing requests to controllers");
app.MapControllers();

try
{
    // Start the application
    logger.LogInformation("Starting the application");
    app.Run();
}
catch (Exception ex)
{
    logger.LogError($"Application failed to start: {ex.Message}");
    throw;
}
