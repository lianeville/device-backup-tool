using DeviceManagerApi.Helpers;
using DeviceManagerApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.Text;
using KoenZomers.UniFi.Api;
using System.Text.Json;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

[Route("api/[controller]")]
[ApiController]
public class DeviceController : ControllerBase
{
	private readonly ILogger<DeviceController> _logger;
	private readonly string _host = Environment.GetEnvironmentVariable("DEVICE_IP");
	private readonly int _port = 22;
	private readonly string _username = "root";
	private readonly string _baseUrl = "https://" + Environment.GetEnvironmentVariable("DEVICE_IP");
	private readonly string _privateKeyPath = "./ssh";
	private readonly string? _privateKeyPassphrase = null;
	private readonly string _uniFiUsername = "admin"; // Your UniFi admin username
	private readonly string _uniFiPassword = "jhoABNdjwV3Gvu4i"; // Your UniFi admin password


	public DeviceController(ILogger<DeviceController> logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		DeviceControllerHelpers.SetLogger(logger);
	}

	#region Endpoints

	[HttpPost("unifi/backup")]
	public async Task<IActionResult> GetUniFiBackup()
	{
		try
		{
			// Create a custom HttpClientHandler to disable SSL validation
			var handler = new HttpClientHandler
			{
				// Disable SSL verification (use cautiously in production)
				ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
			};

			using (var client = new HttpClient(handler))
			{
				// Set the base URL for the UniFi API
				client.BaseAddress = new Uri(_baseUrl);

				// Add headers to accept JSON responses
				client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

				// Define the login payload
				var loginPayload = new
				{
					username = _uniFiUsername,
					password = _uniFiPassword
				};

				// Serialize the payload to JSON
				var jsonContent = new StringContent(
					 JsonSerializer.Serialize(loginPayload),
					 Encoding.UTF8,
					 "application/json"
				);

				// Send the POST request to login
				var loginResponse = await client.PostAsync("/api/auth/login", jsonContent);

				if (!loginResponse.IsSuccessStatusCode)
				{
					var errorContent = await loginResponse.Content.ReadAsStringAsync();
					_logger.LogError($"Login failed: {errorContent}");
					return StatusCode((int)loginResponse.StatusCode, "Failed to log in to UniFi controller.");
				}

				// Extract the token from the login response
				var responseContent = await loginResponse.Content.ReadAsStringAsync();
				var loginJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

				if (!loginJson.TryGetProperty("deviceToken", out var tokenProperty))
				{
					_logger.LogError("Failed to extract device token from login response.");
					return StatusCode(500, "Failed to extract device token from login response.");
				}

				string deviceToken = tokenProperty.GetString();
				_logger.LogInformation($"Received device token: {deviceToken}");

				// Set the Authorization header with the token
				client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", deviceToken);

				// Extract cookies from the login response headers
				string cookies = string.Join("; ", loginResponse.Headers.GetValues("Set-Cookie"));

				// Attempt to extract the x-csrf-token from cookies
				string csrfToken = ExtractCsrfTokenFromCookies(cookies);

				if (!string.IsNullOrEmpty(csrfToken))
				{
					_logger.LogInformation($"Extracted CSRF token: {csrfToken}");
					// Add the x-csrf-token header
					client.DefaultRequestHeaders.Add("x-csrf-token", csrfToken);
				}

				// Define the payload for the backup request
				var backupPayload = new
				{
					cmd = "backup",
					days = -1 // Include all days in the backup
				};

				// Serialize the backup payload
				var backupJsonContent = new StringContent(
					 JsonSerializer.Serialize(backupPayload),
					 Encoding.UTF8,
					 "application/json"
				);

				// Send the POST request to initiate the backup
				var backupResponse = await client.PostAsync("/proxy/network/api/s/default/cmd/backup", backupJsonContent);

				if (!backupResponse.IsSuccessStatusCode)
				{
					var backupErrorContent = await backupResponse.Content.ReadAsStringAsync();
					_logger.LogError($"Backup request failed: {backupErrorContent}");
					return StatusCode((int)backupResponse.StatusCode, "Failed to initiate backup.");
				}

				// Process the successful backup response
				var backupResponseContent = await backupResponse.Content.ReadAsStringAsync();
				_logger.LogInformation("Backup initiated successfully.");
				return Ok(new { backupResponseContent });
			}
		}
		catch (Exception ex)
		{
			// Log and handle unexpected errors
			_logger.LogError($"Error fetching UniFi devices: {ex.Message}");
			return StatusCode(500, "Failed to fetch UniFi devices.");
		}
	}

	private string ExtractCsrfTokenFromCookies(string cookies)
	{
		// Check if the cookies contain "TOKEN"
		if (!string.IsNullOrEmpty(cookies) && cookies.Contains("TOKEN"))
		{
			// Split the cookies to find the TOKEN value
			var cookieParts = cookies.Split(';');
			string token = cookieParts.FirstOrDefault(part => part.Contains("TOKEN"))?.Split('=')[1];

			if (!string.IsNullOrEmpty(token))
			{
				// Split the JWT into its components
				var jwtComponents = token.Split('.');
				if (jwtComponents.Length > 1)
				{
					// Decode the payload (second part of the JWT)
					var payloadJson = Base64UrlDecode(jwtComponents[1]);

					// Parse the payload JSON to extract the csrfToken
					var payload = JsonSerializer.Deserialize<JsonElement>(payloadJson);
					if (payload.TryGetProperty("csrfToken", out var csrfToken))
					{
						return csrfToken.GetString();
					}
				}
			}
		}

		return null; // Return null if no CSRF token is found
	}

	// Helper method to decode Base64Url (used in JWTs)
	private string Base64UrlDecode(string input)
	{
		string base64 = input.Replace('-', '+').Replace('_', '/');
		switch (input.Length % 4)
		{
			case 2: base64 += "=="; break;
			case 3: base64 += "="; break;
		}
		return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
	}


	[HttpGet("script/status")]
	[Authorize]
	public IActionResult GetScriptStatus()
	{
		_logger.LogInformation("Fetching script status from device");

		try
		{
			var scriptStatus = GetScriptStatusFromDevice();
			return Ok(new
			{
				scriptStatus.Status,
				scriptStatus.DateChanged,
				scriptStatus.Memory,
				scriptStatus.Tasks
			});
		}
		catch (Exception ex)
		{
			_logger.LogError($"Failed to fetch script status: {ex.Message}");
			return StatusCode(500, "Failed to fetch script status");
		}
	}

	#endregion
	#region Device

	private Renci.SshNet.ConnectionInfo GetSshConnectionInfo()
	{
		var privateKeyFile = new PrivateKeyFile(_privateKeyPath, _privateKeyPassphrase);
		var keyFiles = new[] { privateKeyFile };

		_logger.LogInformation($"Preparing SSH connection to {_host} on port {_port} with username {_username}");

		return new Renci.SshNet.ConnectionInfo(_host, _port, _username,
			 new PrivateKeyAuthenticationMethod(_username, keyFiles));
	}

	private ScriptStatus GetScriptStatusFromDevice()
	{
		using (var client = new SshClient(GetSshConnectionInfo()))
		{
			try
			{
				_logger.LogInformation($"Attempting to connect to {_host} with username {_username}");
				client.Connect();
				_logger.LogInformation("Connected to device");

				var shellStream = client.CreateShellStream("xterm", 10000, 24, 800, 600, 1024);
				string initialOutput = DeviceControllerHelpers.GetInitialShellOutput(shellStream);
				_logger.LogInformation("Initial shell output (discarded):\n" + initialOutput);

				var commands = new List<(string Command, int ExpectedLines)>
					{
						("systemctl status unifibackup.service", 19)
					};

				string fullOutput = DeviceControllerHelpers.GetCommandOutput(shellStream, commands);
				_logger.LogInformation("Full output from shell commands:\n" + fullOutput);

				client.Disconnect();

				return DeviceControllerHelpers.ParseScriptStatus(fullOutput);
			}
			catch (Exception ex)
			{
				_logger.LogError($"Failed to fetch script status via SSH: {ex.Message}");
				throw;
			}
		}
	}

	#endregion
}
