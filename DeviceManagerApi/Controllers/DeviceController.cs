using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeviceManagerApi.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class DeviceController : ControllerBase
	{
		private readonly ILogger<DeviceController> _logger;
		private readonly string _host = Environment.GetEnvironmentVariable("DEVICE_IP");
		private readonly int _port = 22;
		private readonly string _username = "root";
		private readonly string _privateKeyPath = "./ssh";
		private readonly string? _privateKeyPassphrase = null;

		public DeviceController(ILogger<DeviceController> logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public class UniFiDevice
		{
			public string Name { get; set; } = string.Empty;
			public string Mac { get; set; } = string.Empty;
			public string Model { get; set; } = string.Empty;
			public string IpAddress { get; set; } = string.Empty;
			public string Status { get; set; } = string.Empty;
			public string Version { get; set; } = string.Empty;
			public DateTime LastSeen { get; set; }
		}

		[HttpGet]
		[Authorize]
		public IActionResult GetDevices()
		{
			_logger.LogInformation("Fetching UniFi devices");

			try
			{
				var devices = GetUniFiDevices();
				_logger.LogInformation($"Retrieved {devices.Count} UniFi devices");
				return Ok(devices);
			}
			catch (Exception ex)
			{
				_logger.LogError($"Failed to fetch UniFi devices: {ex.Message}");
				return StatusCode(500, "Failed to fetch device information");
			}
		}

		private Renci.SshNet.ConnectionInfo GetSshConnectionInfo()
		{
			var privateKeyFile = new PrivateKeyFile(_privateKeyPath, _privateKeyPassphrase);
			var keyFiles = new[] { privateKeyFile };

			// Log the connection details for debugging
			_logger.LogInformation($"Preparing SSH connection to {_host} on port {_port} with username {_username}");
			_logger.LogInformation($"Using private key: {_privateKeyPath}");

			if (_privateKeyPassphrase != null)
			{
				_logger.LogInformation("Private key requires a passphrase.");
			}
			else
			{
				_logger.LogInformation("Private key does not require a passphrase.");
			}

			return new Renci.SshNet.ConnectionInfo(_host, _port, _username,
					  new PrivateKeyAuthenticationMethod(_username, keyFiles));
		}

		private List<UniFiDevice> GetUniFiDevices()
		{
			using (var client = new SshClient(GetSshConnectionInfo()))
			{
				try
				{
					_logger.LogInformation($"Attempting to connect to {_host} with username {_username}");
					client.Connect();
					_logger.LogInformation("Connected to UniFi Controller");

					var shellStream = client.CreateShellStream("Shell", 80, 24, 800, 600, 1024);

					// Read and discard initial lines of output before sending commands
					StringBuilder initialOutput = new StringBuilder();
					byte[] buffer = new byte[1024];
					int bytesRead;
					int linesRead = 0;

					while (linesRead < 25)
					{
						bytesRead = shellStream.Read(buffer, 0, buffer.Length);
						if (bytesRead > 0)
						{
							string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
							initialOutput.Append(chunk);
							linesRead += chunk.Split('\n').Length - 1;
						}
						else
						{
							break;
						}
					}

					// Log the initial output (if needed for debugging)
					_logger.LogInformation("Initial shell output (discarded):\n" + initialOutput.ToString());

					// List of commands with their expected number of output lines
					var commands = new List<(string Command, int ExpectedLines)>
					{
						("cd ..", 1),
						("ls", 5),
					};

					StringBuilder fullOutput = new StringBuilder();

					foreach (var (command, expectedLines) in commands)
					{
						shellStream.WriteLine(command);

						// Read the output from the shell stream
						StringBuilder output = new StringBuilder();
						int commandLinesRead = 0;

						while (true)
						{
							bytesRead = shellStream.Read(buffer, 0, buffer.Length);
							if (bytesRead > 0)
							{
								string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
								output.Append(chunk);

								// Count new lines in the chunk
								commandLinesRead += chunk.Split('\n').Length - 1;
								_logger.LogInformation("Printed {commandLinesRead} Lines", commandLinesRead);

								// Break if we've read enough lines
								if (commandLinesRead >= expectedLines)
								{
									_logger.LogInformation($"Expected {expectedLines} lines read for command '{command}'. Exiting loop.");
									break;
								}
							}
							else
							{
								break;
							}
						}

						fullOutput.Append(output);
					}

					_logger.LogInformation("Full output from shell commands:\n" + fullOutput.ToString());

					client.Disconnect();

					return new List<UniFiDevice>();
				}
				catch (Exception ex)
				{
					_logger.LogError($"SSH operation failed: {ex.Message}");
					if (ex is SshAuthenticationException)
					{
						_logger.LogError("Authentication failed. Please verify SSH key path and permissions.");
					}
					throw;
				}
			}
		}
	}
}

