using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Diagnostics;
using StackExchange.Redis;
using static System.Console;
using System.Configuration;
using ServerMonitoringClient.Model;
using Newtonsoft.Json;

namespace ServerMonitoringClient_ForWindows
{
	class Program
	{
		private static readonly string redisServer = "localhost:6379";

		static void Main()
		{
			var allDrives = DriveInfo.GetDrives();
			var redis = ConnectionMultiplexer.Connect(redisServer + ",allowAdmin=true");
			var redisDb = redis.GetDatabase();
			var monitoringCycle = int.Parse(ConfigurationManager.AppSettings["MonitoringCycle"]) * 1000;
			var redisTTLDay = int.Parse(ConfigurationManager.AppSettings["MonitoringSaveDay"]);
			var sw = new Stopwatch();
			while (true)
			{
				sw.Start();
				WriteLine($"[{DateTime.UtcNow.AddHours(9)}] Monitoring Start");
				
				var key = $"{ConfigurationManager.AppSettings["MonitoringServerName"]}_{DateTime.UtcNow.ToString("yyyyMMdd")}";
				var redisPostStr = string.Empty;
				var monitoringTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
				var monitoringCpu = bool.Parse(ConfigurationManager.AppSettings["MonitorCpu"]);

				if (monitoringCpu)
				{
					try
					{
						redisPostStr = "_CPU";
						var cpu = new PerformanceCounter(ConfigurationManager.AppSettings["CpuCounter"], "% Processor Time", "_Total");

						cpu.NextValue();
						Thread.Sleep(1000);

						var monitoringCpuModel = new MonitoringCpuModel
						{
							Time = monitoringTime,
							ProcessTime = (int)cpu.NextValue()
						};

						if (string.IsNullOrEmpty(redisDb.StringGet(key + redisPostStr)))
						{
							var monitoringCpuDateModel = new MonitoringCpuDateModel
							{
								Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
								CpuList = new List<MonitoringCpuModel>()
							};

							monitoringCpuDateModel.CpuList.Add(monitoringCpuModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringCpuDateModel), TimeSpan.FromDays(redisTTLDay));
						}
						else
						{
							var preMonitoringCpuDateModel = JsonConvert.DeserializeObject<MonitoringCpuDateModel>(redisDb.StringGet(key + redisPostStr));

							preMonitoringCpuDateModel.CpuList.Add(monitoringCpuModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(preMonitoringCpuDateModel), TimeSpan.FromDays(redisTTLDay));
						}
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

						redisPostStr = "_MEMORY";
						var wmiObject = new ManagementObjectSearcher("select * from Win32_OperatingSystem");
						var memoryValues = wmiObject.Get().Cast<ManagementObject>().Select(mo => new
						{
							FreePhysicalMemory = Double.Parse(mo["FreePhysicalMemory"].ToString()),
							TotalVisibleMemorySize = Double.Parse(mo["TotalVisibleMemorySize"].ToString())
						}).FirstOrDefault();

						if (memoryValues == null)
							throw new Exception();

						Thread.Sleep(1000);

						var currentRamAvailable = memoryValues.FreePhysicalMemory / 1024 / 1024;
						var CurrentRamUsage = (memoryValues.TotalVisibleMemorySize / 1024 / 1024) - currentRamAvailable;

						var monitoringMemModel = new MonitoringMemoryModel
						{
							Time = monitoringTime,
							AvailableBytes = currentRamAvailable,
							UsageBytes = CurrentRamUsage
						};

						Thread.Sleep(2000);

						if (string.IsNullOrEmpty(redisDb.StringGet(key + redisPostStr)))
						{
							var monitoringMemDateModel = new MonitoringMemoryDateModel
							{
								Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
								MemList = new List<MonitoringMemoryModel>()
							};
							monitoringMemDateModel.MemList.Add(monitoringMemModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringMemDateModel), TimeSpan.FromDays(redisTTLDay));
						}
						else
						{
							var preMonitoringMemoryDateModel = JsonConvert.DeserializeObject<MonitoringMemoryDateModel>(redisDb.StringGet(key + redisPostStr));

							preMonitoringMemoryDateModel.MemList.Add(monitoringMemModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(preMonitoringMemoryDateModel), TimeSpan.FromDays(redisTTLDay));
						}
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
						redisPostStr = "_DISK";

						if (string.IsNullOrEmpty(redisDb.StringGet(key + redisPostStr)))
						{
							var monitoringDiskDateModel = new MonitoringDiskDateModel
							{
								Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
								DiskList = new List<MonitoringDiskModel>()
							};

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

								monitoringDiskDateModel.DiskList.Add(monitoringDiskModel);
							}

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringDiskDateModel), TimeSpan.FromDays(redisTTLDay));
						}
						else
						{
							var preMonitoringDiskDateModel = JsonConvert.DeserializeObject<MonitoringDiskDateModel>(redisDb.StringGet(key + redisPostStr));

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

								preMonitoringDiskDateModel.DiskList.Add(monitoringDiskModel);
							}

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(preMonitoringDiskDateModel), TimeSpan.FromDays(redisTTLDay));
						}
					}
					catch
					{
						WriteLine("Read Failed - Disk");
					}
				}

				var monitoringWeb = bool.Parse(ConfigurationManager.AppSettings["MonitorWeb"]);

				//IIS Web Monitoring Not Kestrel
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

						Thread.Sleep(1000);

						redisPostStr = "_IIS";

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

						if (string.IsNullOrEmpty(redisDb.StringGet(key + redisPostStr)))
						{
							var monitoringWebModel = new MonitoringWebDateModel
							{
								Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
								IisList = new List<MonitoringIisModel>()
							};

							monitoringWebModel.IisList.Add(monitoringIisModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringWebModel), TimeSpan.FromDays(redisTTLDay));
						}
						else
						{
							var preMonitoringWebDateModel = JsonConvert.DeserializeObject<MonitoringWebDateModel>(redisDb.StringGet(key + redisPostStr));

							preMonitoringWebDateModel.IisList.Add(monitoringIisModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(preMonitoringWebDateModel), TimeSpan.FromDays(redisTTLDay));
						}

						redisPostStr = "_WAS";
						try
						{
							var monitoringWasModel = new MonitoringWasModel
							{
								Time = monitoringTime,
								LatencyHealthPing = wasHealthPingReplyLatency.NextValue()

							};

							if (string.IsNullOrEmpty(redisDb.StringGet(key + redisPostStr)))
							{
								var monitoringWebModel = new MonitoringWebDateModel
								{
									Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
									WasList = new List<MonitoringWasModel>()
								};

								monitoringWebModel.WasList.Add(monitoringWasModel);

								redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringWebModel), TimeSpan.FromDays(redisTTLDay));
							}
							else
							{
								var preMonitoringWebDateModel = JsonConvert.DeserializeObject<MonitoringWebDateModel>(redisDb.StringGet(key + redisPostStr));

								preMonitoringWebDateModel.WasList.Add(monitoringWasModel);

								redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(preMonitoringWebDateModel), TimeSpan.FromDays(redisTTLDay));
							}
						}
						catch
						{
							WriteLine("Read Failed - Was Or Iis");
						}

						redisPostStr = "_POOL";

						var monitoringApplicationPoolModel = new MonitoringAppPoolModel
						{
							Time = monitoringTime,
							CurrenttApplicationPoolState = appPoolCurrentState.NextValue(),
							TotalApplicationPoolRecycles = appPoolTotalRecycles.NextValue(),
							WorkerProcessCreated = appPoolTotalWorkerProcessCreated.NextValue()

						};

						if (string.IsNullOrEmpty(redisDb.StringGet(key + redisPostStr)))
						{
							var monitoringWebModel = new MonitoringWebDateModel
							{
								Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
								AppPoolList = new List<MonitoringAppPoolModel>()
							};
							monitoringWebModel.AppPoolList.Add(monitoringApplicationPoolModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringWebModel), TimeSpan.FromDays(redisTTLDay));
						}
						else
						{
							var preMonitoringWebDateModel = JsonConvert.DeserializeObject<MonitoringWebDateModel>(redisDb.StringGet(key + redisPostStr));

							preMonitoringWebDateModel.AppPoolList.Add(monitoringApplicationPoolModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(preMonitoringWebDateModel), TimeSpan.FromDays(redisTTLDay));
						}

					}
					catch
					{
						WriteLine("Read Failed - Web Server");
					}

				}

				var monitorSql = bool.Parse(ConfigurationManager.AppSettings["MonitorSql"]);

				if (monitorSql)
				{
					redisPostStr = "_SQL";
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

						//첫 데이터 안나오는 경우 있음.
						sqlFullScansPerSec.NextValue();
						sqlPageSplitsPerSec.NextValue();
						sqlBufferCacheHitRatio.NextValue();
						sqlUserConnections.NextValue();
						sqlNumberofDeadlocksPerSec.NextValue();
						sqlAverageWaitTime.NextValue();
						sqlTotalServerMemory.NextValue();
						sqlBatchRequestsPerSec.NextValue();

						Thread.Sleep(1000);

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

						if (string.IsNullOrEmpty(redisDb.StringGet(key + redisPostStr)))
						{
							var monitoringSqlDateModel = new MonitoringSqlDateModel
							{
								Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
								SqlList = new List<MonitoringSqlModel>()
							};
							monitoringSqlDateModel.SqlList.Add(monitoringSqlModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(monitoringSqlDateModel), TimeSpan.FromDays(redisTTLDay));
						}
						else
						{
							var preMonitoringSqlDateModel = JsonConvert.DeserializeObject<MonitoringSqlDateModel>(redisDb.StringGet(key + redisPostStr));

							preMonitoringSqlDateModel.SqlList.Add(monitoringSqlModel);

							redisDb.StringSet(key + redisPostStr, JsonConvert.SerializeObject(preMonitoringSqlDateModel), TimeSpan.FromDays(redisTTLDay));
						}

					}
					catch
					{
						WriteLine("Read Failed - SQL Server");
					}
				}
				sw.Stop();
				WriteLine($"[{DateTime.UtcNow.AddHours(9)}] Monitoring End - Will Restart {Math.Max(0, monitoringCycle - sw.ElapsedMilliseconds)} ms");
				Thread.Sleep(Math.Max(0,monitoringCycle - (int)sw.ElapsedMilliseconds));
			}
			
		}
	}
}


