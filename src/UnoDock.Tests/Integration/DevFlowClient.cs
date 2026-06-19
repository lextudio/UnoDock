using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvalonDockTest.Integration
{
	// Thin, reusable HTTP client for the DevFlow agent embedded in the running
	// UnoDock sample (docs/refactoring.md, "DevFlow as test infra"). Integration
	// tests use this to drive deterministic [DevFlowAction] verbs and assert on the
	// structured result, instead of bespoke PowerShell or pixel screenshots.
	//
	// Reachability is opt-in: tests resolve the port from DEVFLOW_TEST_PORT and skip
	// when no sample is running, so the default (headless) test run stays green.
	internal sealed class DevFlowClient : IDisposable
	{
		private readonly HttpClient _http;

		public DevFlowClient(int port)
		{
			_http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
			_http.Timeout = TimeSpan.FromSeconds(15);
		}

		/// <summary>Port from DEVFLOW_TEST_PORT, or null when integration tests should be skipped.</summary>
		public static int? ResolvePortOrNull()
			=> int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out var p) && p > 0
				? p
				: (int?)null;

		public async Task<bool> IsReachableAsync(CancellationToken ct = default)
		{
			try
			{
				using var resp = await _http.GetAsync("/api/v1/agent/status", ct).ConfigureAwait(false);
				return resp.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		public Task<string> GetTreeAsync(CancellationToken ct = default)
			=> GetStringAsync("/api/v1/ui/tree", ct);

		/// <summary>
		/// Invoke a custom [DevFlowAction] and return its string result (the wrapper's
		/// "result"/"value"/"output" field if present, otherwise the raw body).
		/// </summary>
		public async Task<string> InvokeAsync(string action, params object[] args)
		{
			var body = JsonSerializer.Serialize(new { args = args ?? Array.Empty<object>() });
			using var content = new StringContent(body, Encoding.UTF8, "application/json");
			using var resp = await _http.PostAsync($"/api/v1/invoke/actions/{action}", content).ConfigureAwait(false);
			resp.EnsureSuccessStatusCode();
			var raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
			return ExtractResult(raw);
		}

		private async Task<string> GetStringAsync(string path, CancellationToken ct)
		{
			using var resp = await _http.GetAsync(path, ct).ConfigureAwait(false);
			resp.EnsureSuccessStatusCode();
			return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		}

		// DevFlow may return the action's string directly or wrap it in an envelope.
		// Be tolerant of both so tests don't couple to the exact wrapper shape.
		private static string ExtractResult(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw)) return raw;
			try
			{
				using var doc = JsonDocument.Parse(raw);
				if (doc.RootElement.ValueKind == JsonValueKind.Object)
				{
					foreach (var key in new[] { "returnValue", "result", "value", "output", "data" })
					{
						if (doc.RootElement.TryGetProperty(key, out var el))
							return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
					}
				}
				if (doc.RootElement.ValueKind == JsonValueKind.String)
					return doc.RootElement.GetString();
			}
			catch { /* not JSON — fall through to raw */ }
			return raw;
		}

		public void Dispose() => _http.Dispose();
	}

	// Strongly-typed view of the dock-query-layout JSON, so assertions read cleanly.
	internal sealed class DockLayoutSnapshot
	{
		public List<PaneInfo> DocumentPanes { get; } = new();
		public List<PaneInfo> AnchorablePanes { get; } = new();
		public List<FloatingInfo> FloatingWindows { get; } = new();
		public List<string> Hidden { get; } = new();

		public sealed class PaneInfo
		{
			public List<string> Tabs { get; } = new();
			public int Selected { get; set; }
		}

		public sealed class FloatingInfo
		{
			public string Kind { get; set; }
			public List<string> Contents { get; } = new();
		}

		public static DockLayoutSnapshot Parse(string json)
		{
			var snap = new DockLayoutSnapshot();
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			foreach (var p in EnumArray(root, "documentPanes"))
				snap.DocumentPanes.Add(ReadPane(p));
			foreach (var p in EnumArray(root, "anchorablePanes"))
				snap.AnchorablePanes.Add(ReadPane(p));
			foreach (var f in EnumArray(root, "floatingWindows"))
			{
				var fi = new FloatingInfo
				{
					Kind = f.TryGetProperty("kind", out var k) ? k.GetString() : null,
				};
				if (f.TryGetProperty("contents", out var contents))
					foreach (var c in contents.EnumerateArray())
						fi.Contents.Add(c.GetString());
				snap.FloatingWindows.Add(fi);
			}
			if (root.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.Array)
				foreach (var h in hidden.EnumerateArray())
					snap.Hidden.Add(h.GetString());

			return snap;
		}

		private static IEnumerable<JsonElement> EnumArray(JsonElement root, string name)
		{
			if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
				foreach (var e in arr.EnumerateArray())
					yield return e;
		}

		private static PaneInfo ReadPane(JsonElement p)
		{
			var info = new PaneInfo();
			if (p.TryGetProperty("tabs", out var tabs))
				foreach (var t in tabs.EnumerateArray())
					info.Tabs.Add(t.GetString());
			if (p.TryGetProperty("selected", out var sel) && sel.ValueKind == JsonValueKind.Number)
				info.Selected = sel.GetInt32();
			return info;
		}
	}
}
