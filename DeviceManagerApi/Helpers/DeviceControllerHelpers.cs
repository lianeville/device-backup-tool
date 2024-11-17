using DeviceManagerApi.Models;  // Add the using directive to reference ScriptStatus
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DeviceManagerApi.Helpers
{
	public static class DeviceControllerHelpers
	{
		// Static logger field, so you don't need to pass it to every method
		private static ILogger? _logger;

		// Method to set the logger
		public static void SetLogger(ILogger logger)
		{
			_logger = logger;
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
	}
}
