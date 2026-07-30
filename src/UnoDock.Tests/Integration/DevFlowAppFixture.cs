using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AvalonDockTest.Integration
{
	// Auto-launches UnoDock.Sample for the Integration category, mirroring the AvalonDock
	// DevFlowIntegrationTests' DevFlowAppFixture: without this, Integration tests only ran
	// when a human manually built+launched the sample and exported DEVFLOW_TEST_PORT, so by
	// default they were never exercised.
	//
	// If DEVFLOW_TEST_PORT is already set (a sample is running, e.g. launched by hand for
	// live debugging), this fixture does nothing and leaves that instance in place.
	[SetUpFixture]
	public sealed class DevFlowAppFixture
	{
		private const int Port = 9224;
		private Process _process;
		private StringBuilder _stderr;
		private bool _ownsProcess;

		[OneTimeSetUp]
		public async Task OneTimeSetUpAsync()
		{
			if (DevFlowClient.ResolvePortOrNull() != null)
				return; // caller already pointed us at a running sample.

			var samplePath = FindSampleProject();
			if (samplePath == null)
				return; // leave DEVFLOW_TEST_PORT unset; tests will Assert.Ignore individually.

			_stderr = new StringBuilder();
			_process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = ResolveDotnetPath(),
					Arguments = $"run --project \"{samplePath}\" -c Debug -f net10.0-desktop",
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					Environment = { ["DEVFLOW_AGENT_PORT"] = Port.ToString() },
				},
			};
			_process.ErrorDataReceived += (_, e) => { if (e.Data != null) _stderr.AppendLine(e.Data); };
			// Both streams are redirected, so both must be drained: an unread stdout pipe
			// fills up during the build and deadlocks the child process before it ever starts.
			_process.OutputDataReceived += (_, __) => { };

			try
			{
				_process.Start();
			}
			catch (Exception ex)
			{
				_process = null;
				throw new InvalidOperationException($"Failed to launch UnoDock.Sample: {ex.Message}", ex);
			}
			_process.BeginErrorReadLine();
			_process.BeginOutputReadLine();
			_ownsProcess = true;

			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
			var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
			while (DateTimeOffset.UtcNow < deadline)
			{
				if (_process.HasExited)
					throw new InvalidOperationException(
						$"UnoDock.Sample exited during startup.\nStderr:\n{_stderr}");

				try
				{
					using var resp = await http.GetAsync($"http://localhost:{Port}/api/v1/agent/status");
					if (resp.IsSuccessStatusCode)
					{
						Environment.SetEnvironmentVariable("DEVFLOW_TEST_PORT", Port.ToString());
						return;
					}
				}
				catch { /* not up yet */ }

				await Task.Delay(500);
			}

			throw new InvalidOperationException(
				$"UnoDock.Sample did not become reachable on port {Port} within 60s.\nStderr:\n{_stderr}");
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			if (!_ownsProcess || _process == null)
				return;

			try
			{
				if (!_process.HasExited)
				{
					_process.Kill(entireProcessTree: true);
					_process.WaitForExit(5000);
				}
			}
			catch { /* best effort */ }
			finally
			{
				_process.Dispose();
				Environment.SetEnvironmentVariable("DEVFLOW_TEST_PORT", null);
			}
		}

		private static string FindSampleProject()
		{
			var dir = TestContext.CurrentContext.TestDirectory;
			for (var i = 0; i < 8 && dir != null; i++)
			{
				var candidate = Path.Combine(dir, "UnoDock.Sample", "UnoDock.Sample.csproj");
				if (File.Exists(candidate))
					return candidate;
				dir = Path.GetDirectoryName(dir);
			}
			return null;
		}

		private static string ResolveDotnetPath()
		{
			foreach (var candidate in new[]
			{
				"/usr/local/share/dotnet/dotnet",
				"/opt/homebrew/bin/dotnet",
				"/usr/local/share/dotnet/x64/dotnet",
			})
			{
				if (File.Exists(candidate))
					return candidate;
			}

			var envPath = Environment.GetEnvironmentVariable("PATH");
			if (envPath != null)
			{
				foreach (var d in envPath.Split(Path.PathSeparator))
				{
					try
					{
						var candidate = Path.Combine(d, "dotnet");
						if (File.Exists(candidate))
							return candidate;
					}
					catch { }
				}
			}

			return "dotnet";
		}
	}
}
