using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using ServerMonitoringClient.Model;
using StackExchange.Redis;
using static System.Console;

namespace ServerMonitoringClient_ForWindows;

class Program
{
	private static readonly string redisServer = "localhost:6379";

	private async static Task Main()
	{
		var allDrives = DriveInfo.GetDrives();
		var redis = ConnectionMultiplexer.Connect(redisServer + ",allowAdmin=true");
		var redisDb = redis.GetDatabase();
		var monitoringCycle = 1000 * 1000;
		var redisTTLDay = int.Parse(ConfigurationManager.AppSettings["MonitoringSaveDay"]);
		var sw = new Stopwatch();

		while (true)
		{
			sw.Start();

			WriteLine($"[{DateTime.Now.AddHours(9)}] Monitoring Start");

			var serverName = ConfigurationManager.AppSettings["MonitoringServerName"];
			var monitoringServerNames = redisDb.StringGet("Monitoring:ServerName");

			if (string.IsNullOrWhiteSpace(monitoringServerNames))
			{
				var serverNames = new List<string>() { serverName };
				redisDb.StringSet("Monitoring:ServerName", JsonConvert.SerializeObject(serverNames));
			}
			else
			{
				var monitoringServerLists = JsonConvert.DeserializeObject<List<string>>(monitoringServerNames);

				if (!monitoringServerLists.Contains(serverName))
				{
					monitoringServerLists.Add(serverName);
					redisDb.StringSet("Monitoring:ServerName", JsonConvert.SerializeObject(monitoringServerLists));
				}
			}

			var key = $"{ConfigurationManager.AppSettings["MonitoringServerName"]}:{DateTime.UtcNow.ToString("yyyyMMdd")}";
			var redisPostStr = string.Empty;
			var monitoringTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			var monitoringCpu = bool.Parse(ConfigurationManager.AppSettings["MonitorCpu"]);

			if (monitoringCpu)
			{
				try
				{
					redisPostStr = ":CPU";

					var cpu = new PerformanceCounter(ConfigurationManager.AppSettings["CpuCounter"], "% Processor Time", "_Total");

					cpu.NextValue();

					await Task.Delay(1000);

					var monitoringCpuModel = new MonitoringCpuModel
					{
						Time = monitoringTime,
						ProcessTime = (int)cpu.NextValue()
					};

					SaveMonitoringList(redisDb, redisTTLDay, key + redisPostStr, monitoringCpuModel);
				}
				catch
				{
					WriteLine("Read Failed - CPU");
				}
			}

			var monitoringMemory = bool.Parse(ConfigurationManager.AppSettings["MonitorMemory"]);

			if (monitoringMemory)
			{
				try
				{
					redisPostStr = ":MEMORY";

					var wmiObject = new ManagementObjectSearcher("select * from Win32_OperatingSystem");
					var memoryValues = wmiObject.Get().Cast<ManagementObject>().Select(m => new
					{
						FreePhysicalMemory = double.Parse(m["FreePhysicalMemory"].ToString()),
						TotalVisibleMemorySize = double.Parse(m["TotalVisibleMemorySize"].ToString())
					}).FirstOrDefault();

					if (memoryValues == null)
						throw new Exception();

					await Task.Delay(1000);

					var currentRamAvailable = memoryValues.FreePhysicalMemory / 1024 / 1024;
					var CurrentRamUsage = (memoryValues.TotalVisibleMemorySize / 1024 / 1024) - currentRamAvailable;

					var monitoringMemoryModel = new MonitoringMemoryModel
					{
						Time = monitoringTime,
						AvailableBytes = currentRamAvailable,
						UsageBytes = CurrentRamUsage
					};

					SaveMonitoringList(redisDb, redisTTLDay, key + redisPostStr, monitoringMemoryModel);
				}
				catch
				{
					WriteLine("Read Failed - Memory");
				}
			}

			var monitoringDisk = bool.Parse(ConfigurationManager.AppSettings["MonitorDisk"]);

			if (monitoringDisk)
			{
				try
				{
					redisPostStr = ":DISK";

					var savedDiskMonitoringList = redisDb.StringGet(key + redisPostStr);
					var monitoringDiskList = new List<MonitoringDiskModel>();

					if (!string.IsNullOrWhiteSpace(savedDiskMonitoringList))
						monitoringDiskList = JsonConvert.DeserializeObject<List<MonitoringDiskModel>>(savedDiskMonitoringList);

					foreach (var d in allDrives.Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
					{
						var monitoringDiskModel = new MonitoringDiskModel
						{
							Time = monitoringTime,
							Drive = d.Name.Substring(0, 1),
							TotalFreeSpace = d.TotalFreeSpace / 1024 / 1024 / 1024,
							TotalUsedSpace = (d.TotalSize - d.TotalFreeSpace) / 1024 / 1024 / 1024,
							TotalSize = d.TotalSize / 1024 / 1024 / 1024
						};

						monitoringDiskList.Add(monitoringDiskModel);
					}

					redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringDiskList), TimeSpan.FromDays(redisTTLDay));
				}
				catch
				{
					WriteLine("Read Failed - Disk");
				}
			}

			var monitoringWeb = bool.Parse(ConfigurationManager.AppSettings["MonitorWeb"]);

			if (monitoringWeb)
			{
				try
				{
					var iisCurrentConncections = new PerformanceCounter("Web Service", "Current Connections", ConfigurationManager.AppSettings["IISInstance"]);
					var iisServiceUptime = new PerformanceCounter("Web Service", "Service Uptime", ConfigurationManager.AppSettings["IISInstance"]);
					var iisGetRequestPerSec = new PerformanceCounter("Web Service", "Get Requests/sec", ConfigurationManager.AppSettings["IISInstance"]);
					var iisLogonAttemptsPerSec = new PerformanceCounter("Web Service", "Logon Attempts/sec", ConfigurationManager.AppSettings["IISInstance"]);
					var iisNotFoundErrorsPerSec = new PerformanceCounter("Web Service", "Not Found Errors/sec", ConfigurationManager.AppSettings["IISInstance"]);
					var iisPostRequestsPerSec = new PerformanceCounter("Web Service", "Post Requests/sec", ConfigurationManager.AppSettings["IISInstance"]);
					var wasHealthPingReplyLatency = new PerformanceCounter("WAS_W3WP", "Health Ping Reply Latency", "_Total");
					var appPoolCurrentState = new PerformanceCounter("APP_POOL_WAS", "Current Application Pool State", ConfigurationManager.AppSettings["IISInstance"]);
					var appPoolTotalRecycles = new PerformanceCounter("APP_POOL_WAS", "Total Application Pool Recycles", ConfigurationManager.AppSettings["IISInstance"]);
					var appPoolTotalWorkerProcessCreated = new PerformanceCounter("APP_POOL_WAS", "Total Worker Processes Created", ConfigurationManager.AppSettings["IISInstance"]);

					iisCurrentConncections.NextValue();
					iisServiceUptime.NextValue();
					iisGetRequestPerSec.NextValue();
					iisLogonAttemptsPerSec.NextValue();
					iisNotFoundErrorsPerSec.NextValue();
					iisPostRequestsPerSec.NextValue();

					try { wasHealthPingReplyLatency.NextValue(); }
					catch { WriteLine("Read Failed - Was"); }

					appPoolCurrentState.NextValue();
					appPoolTotalRecycles.NextValue();
					appPoolTotalWorkerProcessCreated.NextValue();

					await Task.Delay(1000);

					redisPostStr = ":IIS";

					var monitoringIisModel = new MonitoringIisModel
					{
						Time = monitoringTime,
						currentConnections = iisCurrentConncections.NextValue(),
						ServiceUptime = iisServiceUptime.NextValue(),
						GetRequestCountPerSec = iisGetRequestPerSec.NextValue(),
						LoginAttemptsPerSec = iisLogonAttemptsPerSec.NextValue(),
						NotFoundErrorPerSec = iisNotFoundErrorsPerSec.NextValue(),
						PostRequestCountPerSec = iisPostRequestsPerSec.NextValue()
					};

					SaveMonitoringList(redisDb, redisTTLDay, key + redisPostStr, monitoringIisModel);

					redisPostStr = ":WAS";

					try
					{
						var monitoringWasModel = new MonitoringWasModel
						{
							Time = monitoringTime,
							LatencyHealthPing = wasHealthPingReplyLatency.NextValue()
						};

						var savedWasMonitoringList = redisDb.StringGet(key + redisPostStr);
						var monitoringWasList = new List<MonitoringWasModel>();

						if (!string.IsNullOrWhiteSpace(savedWasMonitoringList))
							monitoringWasList = JsonConvert.DeserializeObject<List<MonitoringWasModel>>(savedWasMonitoringList);

						monitoringWasList.Add(monitoringWasModel);

						redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringWasList), TimeSpan.FromDays(redisTTLDay));
					}
					catch
					{
						WriteLine("Read Failed - Was Or Iis");
					}

					redisPostStr = ":POOL";

					var monitoringApplicationPoolModel = new MonitoringAppPoolModel
					{
						Time = monitoringTime,
						CurrenttApplicationPoolState = appPoolCurrentState.NextValue(),
						TotalApplicationPoolRecycles = appPoolTotalRecycles.NextValue(),
						WorkerProcessCreated = appPoolTotalWorkerProcessCreated.NextValue()
					};

					SaveMonitoringList(redisDb, redisTTLDay, key + redisPostStr, monitoringApplicationPoolModel);
				}
				catch
				{
					WriteLine("Read Failed - Web Server");
				}
			}

			var monitorSql = bool.Parse(ConfigurationManager.AppSettings["MonitorSql"]);

			if (monitorSql)
			{
				redisPostStr = ":SQL";
				try
				{
					var sqlFullScansPerSec = new PerformanceCounter("SQLServer:Access Methods", "Full Scans/sec");
					var sqlPageSplitsPerSec = new PerformanceCounter("SQLServer:Access Methods", "Page Splits/sec");
					var sqlBufferCacheHitRatio = new PerformanceCounter("SQLServer:Buffer Manager", "Buffer cache hit ratio");
					var sqlUserConnections = new PerformanceCounter("SQLServer:General Statistics", "User Connections");
					var sqlNumberofDeadlocksPerSec = new PerformanceCounter("SQLServer:Locks", "Number of Deadlocks/sec", "_Total");
					var sqlAverageWaitTime = new PerformanceCounter("SQLServer:Locks", "Average Wait Time (ms)", "_Total");
					var sqlTotalServerMemory = new PerformanceCounter("SQLServer:Memory Manager", "Total Server Memory (KB)");
					var sqlBatchRequestsPerSec = new PerformanceCounter("SQLServer:SQL Statistics", "Batch Requests/sec");

					sqlFullScansPerSec.NextValue();
					sqlPageSplitsPerSec.NextValue();
					sqlBufferCacheHitRatio.NextValue();
					sqlUserConnections.NextValue();
					sqlNumberofDeadlocksPerSec.NextValue();
					sqlAverageWaitTime.NextValue();
					sqlTotalServerMemory.NextValue();
					sqlBatchRequestsPerSec.NextValue();

					await Task.Delay(1000);

					var monitoringSqlModel = new MonitoringSqlModel
					{
						Time = monitoringTime,
						FullScanPerSec = sqlFullScansPerSec.NextValue(),
						PageSplitPerSec = sqlPageSplitsPerSec.NextValue(),
						BufferCacheHitRatio = sqlBufferCacheHitRatio.NextValue(),
						UserConnections = (int)sqlUserConnections.NextValue(),
						DeadlocksPerSec = (int)sqlNumberofDeadlocksPerSec.NextValue(),
						LockAverageWaitTime = (int)sqlAverageWaitTime.NextValue(),
						TotalServerMemory = (int)sqlTotalServerMemory.NextValue(),
						BatchRequestsPerSec = (int)sqlBatchRequestsPerSec.NextValue(),
					};

					SaveMonitoringList(redisDb, redisTTLDay, key + redisPostStr, monitoringSqlModel);
				}
				catch
				{
					WriteLine("Read Failed - SQL Server");
				}
			}

			sw.Stop();
			WriteLine($"[{DateTime.UtcNow.AddHours(9)}] Monitoring End - Will Restart {Math.Max(0, monitoringCycle - sw.ElapsedMilliseconds)} ms");

			await Task.Delay(Math.Max(0, monitoringCycle - (int)sw.ElapsedMilliseconds));
		}
	}

	private static void SaveMonitoringList<T>(IDatabase redisDb, int redisTTLDay, string key, T monitoringModel)
	{
		var savedMonitoringList = redisDb.StringGet(key);
		var monitoringList = new List<T>();

		if (!string.IsNullOrWhiteSpace(savedMonitoringList))
			monitoringList = JsonConvert.DeserializeObject<List<T>>(savedMonitoringList);

		monitoringList.Add(monitoringModel);

		redisDb.StringSet(key, JsonConvert.SerializeObject(monitoringList), TimeSpan.FromDays(redisTTLDay));
	}
}


