using DeviceManagerApi.Models;
using DeviceManagerApi.Helpers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

using Renci.SshNet;
using Renci.SshNet.Common;

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

using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.GZip;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;


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
	private readonly string _uniFiUsername = "admin"; // Your UniFi admin username
	private readonly string _uniFiPassword = "jhoABNdjwV3Gvu4i"; // Your UniFi admin password


	public DeviceController(ILogger<DeviceController> logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		DeviceControllerHelpers.SetLogger(logger);

	}

	private static byte[] HexStringToByteArray(string hex)
	{
		return Enumerable.Range(0, hex.Length)
							 .Where(x => x % 2 == 0)
							 .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
							 .ToArray();
	}


	public static Stream DecryptBackup(Stream encryptedStream)
	{
		try
		{
			// Convert hex key and IV to byte arrays
			byte[] key = HexStringToByteArray("626379616e676b6d6c756f686d617273");
			byte[] iv = HexStringToByteArray("75626e74656e74657270726973656170");

			// Create AES cipher
			var cipher = new CbcBlockCipher(new AesEngine());
			var parameters = new ParametersWithIV(new KeyParameter(key), iv);
			cipher.Init(false, parameters); // false for decryption

			// Read encrypted data
			byte[] encryptedData = new byte[encryptedStream.Length];
			encryptedStream.Read(encryptedData, 0, encryptedData.Length);

			// Process the encryption block by block
			byte[] decryptedData = new byte[encryptedData.Length];
			int pos = 0;
			int blockSize = cipher.GetBlockSize();

			while (pos < encryptedData.Length)
			{
				cipher.ProcessBlock(encryptedData, pos, decryptedData, pos);
				pos += blockSize;
			}

			// Create memory stream with decrypted data
			var decryptedStream = new MemoryStream(decryptedData);
			decryptedStream.Position = 0;
			return decryptedStream;
		}
		catch (Exception ex)
		{
			throw new CryptographicException("Failed to decrypt backup file", ex);
		}
	}

	public class BsonConverter
	{
		public static JsonDocument ConvertBsonStreamToJson(MemoryStream decompressedStream)
		{
			var documents = new List<BsonDocument>();

			// Read all BSON documents from the stream
			using (var reader = new BsonBinaryReader(decompressedStream))
			{
				try
				{
					while (decompressedStream.Position < decompressedStream.Length)
					{
						var document = BsonSerializer.Deserialize<BsonDocument>(reader);
						documents.Add(document);
					}
				}
				catch (EndOfStreamException)
				{
					// Expected when we reach the end of the stream
				}
			}

			// Create a root object to hold all documents
			var rootObject = new Dictionary<string, object>();

			// Group documents by their collection (_type field if present)
			var groupedDocuments = documents
				 .GroupBy(doc => doc.Contains("_type") ?
									doc["_type"].AsString :
									"unclassified");

			// Add each group to the root object
			foreach (var group in groupedDocuments)
			{
				var collectionName = group.Key;
				var documentList = group.Select(doc => doc.ToJson()).ToList();
				rootObject[collectionName] = documentList;
			}

			// Convert to JSON
			var jsonString = JsonSerializer.Serialize(rootObject, new JsonSerializerOptions
			{
				WriteIndented = true
			});

			return JsonDocument.Parse(jsonString);
		}
	}

	#region Endpoints

	[HttpPost("unifi/backup/start")]
	[EnableCors("AllowSpecificOrigin")]
	public async Task<IActionResult> StartUniFiBackup()
	{
		try
		{
			var backupId = Guid.NewGuid().ToString();
			_ = ProcessBackupAsync(backupId);
			return Ok(new { backupId });
		}
		catch (Exception ex)
		{
			return BadRequest(new { message = ex.Message });
		}
	}

	[HttpGet("unifi/backup/status/{backupId}")]
	[EnableCors("AllowSpecificOrigin")]
	public async Task GetBackupStatus(string backupId)
	{
		var response = Response;
		response.Headers.Add("Content-Type", "text/event-stream");
		response.Headers.Add("Cache-Control", "no-cache");
		response.Headers.Add("Connection", "keep-alive");
		Response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5173");
		Response.Headers.Add("Access-Control-Allow-Credentials", "true");

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

				await SendProgress("Attempting to login to UniFi controller...");

				var loginPayload = new { username = _uniFiUsername, password = _uniFiPassword };
				var jsonContent = new StringContent(
					 JsonSerializer.Serialize(loginPayload),
					 Encoding.UTF8,
					 "application/json"
				);

				var loginResponse = await client.PostAsync("/api/auth/login", jsonContent);

				if (!loginResponse.IsSuccessStatusCode)
				{
					var errorContent = await loginResponse.Content.ReadAsStringAsync();
					await SendProgress($"Login failed: {errorContent}", "error");
					return;
				}

				await SendProgress("Successfully logged in to UniFi controller");

				var responseContent = await loginResponse.Content.ReadAsStringAsync();
				var loginJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

				if (!loginJson.TryGetProperty("deviceToken", out var tokenProperty))
				{
					await SendProgress("Failed to extract device token", "error");
					return;
				}

				string deviceToken = tokenProperty.GetString();
				await SendProgress("Successfully obtained device token");

				client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", deviceToken);

				string cookies = string.Join("; ", loginResponse.Headers.GetValues("Set-Cookie"));
				string csrfToken = ExtractCsrfTokenFromCookies(cookies);

				if (!string.IsNullOrEmpty(csrfToken))
				{
					client.DefaultRequestHeaders.Add("x-csrf-token", csrfToken);
					await SendProgress("Successfully extracted and set CSRF token");
				}

				await SendProgress("Initiating backup process...");

				// var backupPayload = new { cmd = "backup", days = -1 };
				// var backupJsonContent = new StringContent(
				// 	  JsonSerializer.Serialize(backupPayload),
				// 	  Encoding.UTF8,
				// 	  "application/json"
				// );

				// var backupResponse = await client.PostAsync("/proxy/network/api/s/default/cmd/backup", backupJsonContent);

				// if (!backupResponse.IsSuccessStatusCode)
				// {
				// 	var backupErrorContent = await backupResponse.Content.ReadAsStringAsync();
				// 	await SendProgress($"Backup request failed: {backupErrorContent}", "error");
				// 	return;
				// }


				await SendProgress("Backup created successfully, downloading file...");

				string remoteBackupFilePath = "/data/unifi/data/backup/8.6.9.unf";
				var fileStream = GetBackupFileStream(remoteBackupFilePath);

				if (fileStream == null)
				{
					await SendProgress("Failed to download backup file", "error");
					return;
				}

				await SendProgress($"Downloaded backup file ({fileStream.Length} bytes)");
				await SendProgress("Decrypting backup file...");

				try
				{
					var decryptedStream = DecryptBackup(fileStream);
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
								await SendProgress($"Found db.gz file (Size: {entry.Size} bytes)");

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

									var jsonResult = BsonConverter.ConvertBsonStreamToJson(decompressedStream);

									var finalResult = JsonSerializer.Serialize(new
									{
										status = "complete",
										data = jsonResult
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

	private async Task ProcessBackupAsync(string backupId)
	{
		// This method can be used to store backup state if needed
		await Task.CompletedTask;
	}
	private bool FileExistsOnRemote(SshClient sshClient, string remoteFilePath)
	{
		try
		{
			// Run a shell command to check if the file exists
			var command = sshClient.RunCommand($"test -e {remoteFilePath} && echo 'exists' || echo 'not exists'");

			// If the result is 'exists', return true, otherwise false
			return command.Result.Trim() == "exists";
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error checking if file exists on remote: {ex.Message}");
			return false;
		}
	}

	private Stream GetBackupFileStream(string remoteFilePath)
	{
		try
		{
			using (var sshClient = new SshClient(GetSshConnectionInfo()))
			{
				sshClient.Connect();

				// Check if the backup file exists on the remote device using SshClient
				if (!FileExistsOnRemote(sshClient, remoteFilePath))
				{
					_logger.LogError($"Backup file does not exist at {remoteFilePath}.");
					return null;
				}

				// Use ScpClient to download the file content to a memory stream instead of a file
				using (var scpClient = new ScpClient(GetSshConnectionInfo()))
				{
					scpClient.Connect();
					var memoryStream = new MemoryStream();
					scpClient.Download(remoteFilePath, memoryStream);

					_logger.LogInformation($"Backup file fetched from {remoteFilePath}. Returning to the client.");
					sshClient.Disconnect();

					// Set the memory stream position to the beginning before returning
					memoryStream.Position = 0;
					return memoryStream;
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error downloading backup file: {ex.Message}");
			return null;
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
	[EnableCors("AllowSpecificOrigin")]
	public IActionResult GetScriptStatus()
	{
		_logger.LogInformation("Fetching script status from device");

		Response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5173");
		Response.Headers.Add("Access-Control-Allow-Credentials", "true");

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