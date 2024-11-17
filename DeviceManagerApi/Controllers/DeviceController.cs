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
		DeviceControllerHelpers.SetLogger(logger);
	}

	#region Endpoints

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
