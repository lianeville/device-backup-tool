using DeviceManagerApi.Models;
using DeviceManagerApi.Helpers;

using Microsoft.AspNetCore.Mvc;
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

using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.GZip;
using System.Linq;
using System.Security.Cryptography;
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

				// Now, initiate the SSH file download
				string remoteBackupFilePath = "/data/unifi/data/backup/8.6.9.unf"; // Adjust to actual backup file path on the device
				var fileStream = GetBackupFileStream(remoteBackupFilePath);

				if (fileStream == null)
				{
					return StatusCode(500, "Failed to download backup file.");
				}

				_logger.LogInformation($"Backup file size: {fileStream.Length} bytes.");

				try
				{
					var decryptedStream = DecryptBackup(fileStream);
					_logger.LogInformation("Backup file decrypted successfully.");

					// Create a temporary memory stream to store the decrypted data
					var tempMemoryStream = new MemoryStream();
					await decryptedStream.CopyToAsync(tempMemoryStream);
					tempMemoryStream.Position = 0;

					// Use SharpZipLib to read the ZIP file
					using (var zipInputStream = new ZipInputStream(tempMemoryStream))
					{
						ZipEntry entry;
						while ((entry = zipInputStream.GetNextEntry()) != null)
						{
							if (entry.Name.Equals("db.gz", StringComparison.OrdinalIgnoreCase))
							{
								_logger.LogInformation($"Found db.gz file in archive. Size: {entry.Size} bytes");

								// Create a memory stream to store the db.gz content
								var gzippedStream = new MemoryStream();
								byte[] buffer = new byte[4096];
								int count;
								while ((count = zipInputStream.Read(buffer, 0, buffer.Length)) > 0)
								{
									gzippedStream.Write(buffer, 0, count);
								}

								gzippedStream.Position = 0;
								_logger.LogInformation("Successfully extracted db.gz from backup archive");

								// Now decompress the GZip content
								using (var gzipInputStream = new GZipInputStream(gzippedStream))
								{
									var decompressedStream = new MemoryStream();
									buffer = new byte[4096];
									while ((count = gzipInputStream.Read(buffer, 0, buffer.Length)) > 0)
									{
										decompressedStream.Write(buffer, 0, count);
									}

									decompressedStream.Position = 0;
									_logger.LogInformation($"Successfully decompressed db.gz. Final size: {decompressedStream.Length} bytes");

									var jsonResult = BsonConverter.ConvertBsonStreamToJson(decompressedStream);
									return Ok(jsonResult);
								}
							}
						}

						_logger.LogError("db.gz file not found in the backup archive");
						return NotFound("db.gz file not found in the backup");
					}
				}
				catch (ICSharpCode.SharpZipLib.SharpZipBaseException ex)
				{
					_logger.LogError($"Failed to process ZIP archive using SharpZipLib: {ex.Message}");
					return StatusCode(500, "Failed to process backup archive");
				}
				catch (CryptographicException ex)
				{
					_logger.LogError($"Failed to decrypt backup file: {ex.Message}");
					return StatusCode(500, "Failed to decrypt backup file");
				}
			}
		}
		catch (Exception ex)
		{
			// Log and handle unexpected errors
			_logger.LogError($"Error fetching UniFi devices: {ex.Message}");
			return StatusCode(500, "Failed to fetch UniFi devices.");
		}
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
