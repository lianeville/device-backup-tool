using DeviceManagerApi.Models;  // Add the using directive to reference ScriptStatus
using Renci.SshNet;
using Renci.SshNet.Common;

using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.GZip;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Linq;



namespace DeviceManagerApi.Helpers
{
	public static class ControllerHelpers
	{
		// Static logger field, so you don't need to pass it to every method
		private static ILogger? _logger;

		// Method to set the logger
		public static void SetLogger(ILogger logger)
		{
			_logger = logger;
		}

		public static string GetHostname(string inputString)
		{
			string collectionSearch = $"{{ \"__cmd\" : \"select\", \"collection\" : \"setting\" }}";
			string variableSearch = $"\"hostname\"";

			int firstIndex = inputString.IndexOf(collectionSearch);
			if (firstIndex == -1)
			{
				_logger.LogError("Could not find collection.");
				return "Error";
			}

			int secondIndex = inputString.IndexOf(variableSearch, firstIndex + collectionSearch.Length);
			if (secondIndex == -1)
			{
				_logger.LogError("Could not find variable.");
				return "Error";
			}

			// Find the next set of double quotes after the second search term
			int startQuote = inputString.IndexOf("\"", secondIndex + variableSearch.Length);
			int endQuote = inputString.IndexOf("\"", startQuote + 1);

			if (startQuote != -1 && endQuote != -1)
			{
				string extractedString = inputString.Substring(startQuote + 1, endQuote - startQuote - 1);
				return extractedString;
			}

			_logger.LogError("Could not find hostname.");
			return "Could not find hostname.";
		}

		public static List<Dictionary<string, string>> GetAlerts(string inputString)
		{
			var alerts = new List<Dictionary<string, string>>();
			string collectionSearch = $"{{ \"__cmd\" : \"select\", \"collection\" : \"alert\" }}";

			int currentSearchIndex = inputString.IndexOf(collectionSearch);
			if (currentSearchIndex == -1)
			{
				_logger.LogError("Could not find alert collection.");
				return alerts;
			}

			int maxAlerts = 3;
			while (alerts.Count < maxAlerts)
			{
				// Search for 'key'
				string keySearch = "\"key\"";
				int keyIndex = inputString.IndexOf(keySearch, currentSearchIndex);
				if (keyIndex == -1) break;

				// Find key value
				int keyStartQuote = inputString.IndexOf("\"", keyIndex + keySearch.Length);
				int keyEndQuote = inputString.IndexOf("\"", keyStartQuote + 1);

				if (keyStartQuote == -1 || keyEndQuote == -1) break;

				string key = inputString.Substring(keyStartQuote + 1, keyEndQuote - keyStartQuote - 1);

				// Search for 'time'
				string timeSearch = "\"time\"";
				int timeIndex = inputString.IndexOf(timeSearch, keyEndQuote);
				if (timeIndex == -1) break;

				// Find time value
				int timeStartQuote = inputString.IndexOf("\"", timeIndex + timeSearch.Length);
				int timeEndQuote = inputString.IndexOf("\"", timeStartQuote + 1);

				if (timeStartQuote == -1 || timeEndQuote == -1) break;

				string time = inputString.Substring(timeStartQuote + 1, timeEndQuote - timeStartQuote - 1);

				// Add to alerts list
				alerts.Add(new Dictionary<string, string>
		  	{
				{ "key", key },
				{ "time", time }
		  	});

				// Update search index for next iteration
				currentSearchIndex = timeEndQuote;
			}

			if (alerts.Count == 0)
			{
				_logger.LogWarning("No alerts found.");
			}

			return alerts;
		}

		public static List<string> SearchString(string inputString, List<(string, string)> searches)
		{
			var results = new List<string>();

			foreach (var (firstSearch, secondSearch) in searches)
			{
				string firstSearchFull = $"{{ \"__cmd\" : \"select\", \"collection\" : \"{firstSearch}\" }}";
				string secondSearchFull = $"\"{secondSearch}\"";

				int firstIndex = inputString.IndexOf(firstSearchFull);
				if (firstIndex == -1) continue;

				int secondIndex = inputString.IndexOf(secondSearchFull, firstIndex + firstSearchFull.Length);
				if (secondIndex == -1) continue;

				// Find the next set of double quotes after the second search term
				int startQuote = inputString.IndexOf("\"", secondIndex + secondSearchFull.Length);
				int endQuote = inputString.IndexOf("\"", startQuote + 1);

				if (startQuote != -1 && endQuote != -1)
				{
					string extractedString = inputString.Substring(startQuote + 1, endQuote - startQuote - 1);
					results.Add(extractedString);
				}
			}

			return results;
		}

		public static ScriptStatus ParseScriptStatus(string fullOutput)
		{
			if (_logger == null) throw new InvalidOperationException("Logger is not set.");

			ScriptStatus status = new ScriptStatus();
			_logger.LogInformation("fullOutput: {fullOutput}", fullOutput);

			status.Status = fullOutput.Contains("running");
			status.Memory = ExtractInt(fullOutput, @"(?<=Memory:\s)(\d+)(?=\.)");

			// Extract date
			string datePattern = @"\b(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\b";
			Match dateMatch = Regex.Match(fullOutput, datePattern);
			if (dateMatch.Success)
			{
				status.DateChanged = DateTime.Parse(dateMatch.Value);
			}
			else
			{
				_logger.LogWarning("No valid date found.");
			}

			return status;
		}

		public static int ExtractInt(string input, string pattern)
		{
			if (_logger == null) throw new InvalidOperationException("Logger is not set.");

			int num = -1;
			Regex regex = new Regex(pattern);
			Match match = regex.Match(input);

			if (match.Success && match.Groups.Count > 1)
			{
				string numStr = match.Groups[1].Value;
				if (int.TryParse(numStr, out num))
				{
					return num;
				}
				else
				{
					_logger.LogError("Failed to parse number: '{0}'", numStr);
				}
			}
			else
			{
				_logger.LogError("No match found for pattern: {0}", pattern);
			}

			return num;
		}

		public static async Task ProcessBackupAsync(string backupId)
		{
			// This method can be used to store backup state if needed
			await Task.CompletedTask;
		}
		private static bool FileExistsOnRemote(SshClient sshClient, string remoteFilePath)
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

		public static Stream GetBackupFileStream(string remoteFilePath, Renci.SshNet.ConnectionInfo ConnectionInfo)
		{
			try
			{
				using (var sshClient = new SshClient(ConnectionInfo))
				{
					sshClient.Connect();

					// Check if the backup file exists on the remote device using SshClient
					if (!FileExistsOnRemote(sshClient, remoteFilePath))
					{
						_logger.LogError($"Backup file does not exist at {remoteFilePath}.");
						return null;
					}

					// Use ScpClient to download the file content to a memory stream instead of a file
					using (var scpClient = new ScpClient(ConnectionInfo))
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

		public static string ExtractCsrfTokenFromCookies(string cookies)
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
		private static string Base64UrlDecode(string input)
		{
			string base64 = input.Replace('-', '+').Replace('_', '/');
			switch (input.Length % 4)
			{
				case 2: base64 += "=="; break;
				case 3: base64 += "="; break;
			}
			return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
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
				int totalBytesRead = 0;

				while (totalBytesRead < encryptedData.Length)
				{
					int bytesRead = encryptedStream.Read(encryptedData, totalBytesRead, encryptedData.Length - totalBytesRead);
					if (bytesRead == 0)
					{
						throw new EndOfStreamException("Unable to read the entire stream.");
					}
					totalBytesRead += bytesRead;
				}

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

		private static byte[] HexStringToByteArray(string hex)
		{
			return Enumerable.Range(0, hex.Length)
								 .Where(x => x % 2 == 0)
								 .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
								 .ToArray();
		}

		public static string GetInitialShellOutput(ShellStream shellStream)
		{
			if (_logger == null) throw new InvalidOperationException("Logger is not set.");

			StringBuilder initialOutput = new StringBuilder();
			byte[] buffer = new byte[1024];
			int bytesRead;
			int linesRead = 0;

			// Read and discard initial lines of output before sending commands
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

			return initialOutput.ToString();
		}

		public static string GetCommandOutput(ShellStream shellStream, List<(string Command, int ExpectedLines)> commands)
		{
			if (_logger == null) throw new InvalidOperationException("Logger is not set.");

			StringBuilder fullOutputBuilder = new StringBuilder();

			foreach (var (command, expectedLines) in commands)
			{
				shellStream.WriteLine(command);
				int linesRead = 0;

				while (linesRead < expectedLines)
				{
					byte[] buffer = new byte[1024];
					int bytesRead = shellStream.Read(buffer, 0, buffer.Length);
					if (bytesRead > 0)
					{
						string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
						var lines = chunk.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

						foreach (var line in lines)
						{
							fullOutputBuilder.AppendLine(line);
							linesRead++;

							if (linesRead >= expectedLines)
							{
								_logger.LogInformation($"Reached expected {expectedLines} lines for command '{command}'. Exiting loop.");
								break;
							}
						}
					}
					else
					{
						break;
					}
				}
			}

			return fullOutputBuilder.ToString();
		}

		public static string ConvertBsonStreamToString(MemoryStream decompressedStream)
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

			// Convert to a single string
			var stringBuilder = new StringBuilder();

			foreach (var group in rootObject)
			{
				stringBuilder.AppendLine($"Collection: {group.Key}");
				foreach (var doc in (List<string>)group.Value)
				{
					stringBuilder.AppendLine(doc);
				}
				stringBuilder.AppendLine(); // Add a blank line between collections
			}

			return stringBuilder.ToString();
		}
	}
}
