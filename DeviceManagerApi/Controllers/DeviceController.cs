using DeviceManagerApi.Models;
using DeviceManagerApi.Helpers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

using Renci.SshNet;
using Renci.SshNet.Common;

using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.GZip;

using System;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Linq;

[Route("api/[controller]")]
[ApiController]

public class DeviceController : ControllerBase
{
	private readonly ILogger<DeviceController> _logger;
	private readonly string _host = Environment.GetEnvironmentVariable("DEVICE_IP");
	private readonly int _port = 22;
	private readonly string _username = "root";
	private readonly string _baseUrl = "https://" + Environment.GetEnvironmentVariable("DEVICE_IP");
	private readonly string _privateKeyPath = "./.ssh/id_rsa";
	private readonly string? _privateKeyPassphrase = null;
	private readonly string _uniFiUsername = Environment.GetEnvironmentVariable("UNIFI_USERNAME");
	private readonly string _uniFiPassword = Environment.GetEnvironmentVariable("UNIFI_PASSWORD");


	public DeviceController(ILogger<DeviceController> logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		ControllerHelpers.SetLogger(logger);

		_logger.LogInformation(_uniFiUsername);
		_logger.LogInformation(_uniFiPassword);

	}

	#region Endpoints

	[HttpPost("unifi/backup/start")]
	[EnableCors("AllowSpecificOrigin")]
	public async Task<IActionResult> StartUniFiBackup()
	{
		try
		{
			var backupId = Guid.NewGuid().ToString();
			_ = ControllerHelpers.ProcessBackupAsync(backupId);
			return Ok(new { backupId });
		}
		catch (Exception ex)
		{
			return BadRequest(new { message = ex.Message });
		}
	}

	[HttpGet("unifi/backup/status/{backupId}/{regenerate}")]
	[EnableCors("AllowSpecificOrigin")]
	public async Task GetBackupStatus(string backupId, string regenerate)
	{
		var response = Response;
		response.Headers["Content-Type"] = "text/event-stream";
		response.Headers["Cache-Control"] = "no-cache";
		response.Headers["Connection"] = "keep-alive";
		response.Headers["Access-Control-Allow-Origin"] = "http://localhost:5173";
		response.Headers["Access-Control-Allow-Credentials"] = "true";

		bool shouldRegenerateBackup;
		if (!bool.TryParse(regenerate, out shouldRegenerateBackup))
		{
			_logger.LogError("Invalid regenerate value");
		}

		try
		{
			async Task SendProgress(string message, string status = "progress")
			{
				var progressData = JsonSerializer.Serialize(new { message, status });
				await response.WriteAsync($"data: {progressData}\n\n");
				await response.Body.FlushAsync();
				_logger.LogInformation(message);
			}

			await SendProgress("Initializing backup process...");

			var handler = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
			};

			using (var client = new HttpClient(handler))
			{
				client.BaseAddress = new Uri(_baseUrl);
				client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

				// [Login process remains the same as in the original code]
				// ... [Previous login code]

				string remoteBackupFilePath = "/data/unifi/data/backup/8.6.9.unf";
				var fileStream = ControllerHelpers.GetBackupFileStream(remoteBackupFilePath, GetSshConnectionInfo());

				if (fileStream == null)
				{
					await SendProgress("Failed to download backup file", "error");
					return;
				}

				await SendProgress($"Downloaded backup file ({fileStream.Length} bytes)");

				// Convert fileStream to base64 for transmission
				fileStream.Position = 0;
				byte[] backupFileBytes;
				using (var memoryStream = new MemoryStream())
				{
					await fileStream.CopyToAsync(memoryStream);
					backupFileBytes = memoryStream.ToArray();
				}
				string base64BackupFile = Convert.ToBase64String(backupFileBytes);

				await SendProgress("Decrypting backup file...");

				try
				{
					var decryptedStream = ControllerHelpers.DecryptBackup(new MemoryStream(backupFileBytes));
					await SendProgress("Backup file decrypted successfully");

					var tempMemoryStream = new MemoryStream();
					await decryptedStream.CopyToAsync(tempMemoryStream);
					tempMemoryStream.Position = 0;

					await SendProgress("Processing backup archive...");

					using (var zipInputStream = new ZipInputStream(tempMemoryStream))
					{
						ZipEntry entry;
						while ((entry = zipInputStream.GetNextEntry()) != null)
						{
							if (entry.Name.Equals("db.gz", StringComparison.OrdinalIgnoreCase))
							{
								await SendProgress($"Found db.gz file");

								var gzippedStream = new MemoryStream();
								byte[] buffer = new byte[4096];
								int count;
								while ((count = zipInputStream.Read(buffer, 0, buffer.Length)) > 0)
								{
									gzippedStream.Write(buffer, 0, count);
								}

								gzippedStream.Position = 0;
								await SendProgress("Decompressing database...");

								using (var gzipInputStream = new GZipInputStream(gzippedStream))
								{
									var decompressedStream = new MemoryStream();
									buffer = new byte[4096];
									while ((count = gzipInputStream.Read(buffer, 0, buffer.Length)) > 0)
									{
										decompressedStream.Write(buffer, 0, count);
									}

									decompressedStream.Position = 0;
									await SendProgress($"Database decompressed successfully ({decompressedStream.Length} bytes)");

									await SendProgress("Converting BSON to JSON...");

									string sourceText = ControllerHelpers.ConvertBsonStreamToString(decompressedStream);

									var (alerts, remainingAlerts) = ControllerHelpers.GetAlerts(sourceText);
									var results = new
									{
										Hostname = ControllerHelpers.GetHostname(sourceText),
										Alerts = alerts,
										RemainingAlerts = remainingAlerts,
										Date = DateTime.Now,
										SourceText = sourceText,
										OriginalBackupFile = new
										{
											FileName = Path.GetFileName(remoteBackupFilePath),
											FileSizeBytes = backupFileBytes.Length,
											Base64Content = base64BackupFile
										}
									};

									var finalResult = JsonSerializer.Serialize(new
									{
										message = "Backup file retrieved",
										status = "complete",
										data = results,
									});
									await response.WriteAsync($"data: {finalResult}\n\n");
									return;
								}
							}
						}

						await SendProgress("db.gz file not found in the backup archive", "error");
					}
				}
				catch (Exception ex)
				{
					await SendProgress($"Error processing backup: {ex.Message}", "error");
				}
			}
		}
		catch (Exception ex)
		{
			await response.WriteAsync($"data: {JsonSerializer.Serialize(new { message = $"Error: {ex.Message}", status = "error" })}\n\n");
		}
	}

	[HttpGet("script/status")]
	[EnableCors("AllowSpecificOrigin")]
	public IActionResult GetScriptStatus()
	{
		_logger.LogInformation("Fetching script status from device");

		Response.Headers["Access-Control-Allow-Origin"] = "http://localhost:5173";
		Response.Headers["Access-Control-Allow-Credentials"] = "true";

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
				string initialOutput = ControllerHelpers.GetInitialShellOutput(shellStream);
				_logger.LogInformation("Initial shell output (discarded):\n" + initialOutput);

				var commands = new List<(string Command, int ExpectedLines)>
					{
						("systemctl status unifibackup.service", 19)
					};

				string fullOutput = ControllerHelpers.GetCommandOutput(shellStream, commands);
				_logger.LogInformation("Full output from shell commands:\n" + fullOutput);

				client.Disconnect();

				return ControllerHelpers.ParseScriptStatus(fullOutput);
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