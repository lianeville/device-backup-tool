namespace DeviceManagerApi.Models
{
	public class ScriptStatus
	{
		public bool Status { get; set; }
		public DateTime DateChanged { get; set; }
		public float Memory { get; set; }
		public int Tasks { get; set; }

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
	// public class Backup
	// {

	// }

}
