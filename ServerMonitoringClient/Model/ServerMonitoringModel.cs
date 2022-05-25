using System.Collections.Generic;

namespace ServerMonitoringClient.Model
{
	public class MonitoringCpuModel
	{
		public string Time { get; set; }
		public int ProcessTime { get; set; }
	}

	public class MonitoringMemoryModel
	{
		public string Time { get; set; }
		public double UsageBytes { get; set; }
		public double AvailableBytes { get; set; }
	}

	public class MonitoringDiskModel
	{
		public string Time { get; set; }
		public string Drive { get; set; }
		public double TotalFreeSpace { get; set; }
		public double TotalUsedSpace { get; set; }
		public double TotalSize { get; set; }
	}

	public class MonitoringIisModel
	{
		public string Time { get; set; }
		public double currentConnections { get; set; }
		public double ServiceUptime { get; set; }
		public double GetRequestCountPerSec { get; set; }
		public double LoginAttemptsPerSec { get; set; }
		public double NotFoundErrorPerSec { get; set; }
		public double PostRequestCountPerSec { get; set; }
	}

	public class MonitoringWasModel
	{
		public string Time { get; set; }
		public double LatencyHealthPing { get; set; }
	}

	public class MonitoringAppPoolModel
	{
		public string Time { get; set; }
		public double CurrenttApplicationPoolState { get; set; }
		public double TotalApplicationPoolRecycles { get; set; }
		public double WorkerProcessCreated { get; set; }
	}

	public class MonitoringSqlModel
	{
		public string Time { get; set; }
		public double FullScanPerSec { get; set; }
		public double PageSplitPerSec { get; set; }
		public double BufferCacheHitRatio { get; set; }
		public int UserConnections { get; set; }
		public int DeadlocksPerSec { get; set; }
		public int LockAverageWaitTime { get; set; }
		public int TotalServerMemory { get; set; }
		public int BatchRequestsPerSec { get; set; }
	}
}
