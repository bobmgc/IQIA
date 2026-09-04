using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.Research.NewDataSourceFeasibility;

/// <summary>
/// READ-ONLY connectivity probe for QDE-013 (feasibility of a new data source). Does NOT integrate any
/// API, modifies no production type, is never wired into a production flow. It reuses the existing
/// internal <see cref="HttpYahooChartClient"/> (unchanged) to confirm which cross-asset / implied-vol
/// tickers Yahoo actually serves at 5m/15m/1h and over what history, plus one raw GET to the Yahoo
/// options endpoint to confirm it is a snapshot (no history). Output: a plain report used to fill the
/// QDE-013 feasibility matrix. Skips cleanly when Yahoo is unavailable.
/// </summary>
public sealed class YahooCrossAssetProbeTests
{
    private readonly ITestOutputHelper _output;
    public YahooCrossAssetProbeTests(ITestOutputHelper output) => _output = output;

    // (label, Yahoo ticker, why it might matter)
    private static readonly (string Label, string Ticker, string Rationale)[] Tickers =
    {
        ("VIX (implied vol index)",        "^VIX",      "CBOE 30-day implied vol - the simplest IV proxy"),
        ("VIX9D",                          "^VIX9D",    "short-dated IV, term-structure front"),
        ("VIX3M",                          "^VIX3M",    "3-month IV, term-structure back"),
        ("S&P 500 cash index",             "^GSPC",     "underlying of MES/ES - RTH only, no overnight"),
        ("10Y Treasury yield",             "^TNX",      "rates / risk-on-off"),
        ("US Dollar Index (ICE spot)",     "DX-Y.NYB",  "USD strength"),
        ("US Dollar Index (ICE future)",   "DX=F",      "USD strength, futures form"),
        ("10Y T-Note future",              "ZN=F",      "bonds / flight to quality"),
        ("30Y T-Bond future",              "ZB=F",      "long bonds"),
        ("EUR/USD future",                 "6E=F",      "FX cross"),
        ("Nasdaq-100 future",              "NQ=F",      "correlated equity index (already mapped)"),
        ("Gold future",                    "GC=F",      "already mapped, sanity check"),
    };

    private static readonly string[] Intervals = { "5m", "15m", "1h" };

    private readonly List<string> _log = new();
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    [Fact]
    public void Probe_Yahoo_CrossAsset_And_ImpliedVol_Availability()
    {
        var client = new HttpYahooChartClient();
        DateTime now = DateTime.UtcNow;

        W($"QDE-013 Yahoo cross-asset / implied-vol connectivity probe  ({now:O})");
        W("Method: internal HttpYahooChartClient.FetchChartJson (unchanged) + YahooChartParser.Parse.");
        W("Request window: 5m/15m -> last 58 days ; 1h -> last 700 days.");
        W("");
        W(string.Format("{0,-28} {1,-11} {2,-9} {3,-9} {4}", "instrument", "interval", "bars", "spanDays", "range / error"));
        W(new string('-', 100));

        int anyOk = 0;
        foreach ((string label, string ticker, string _) in Tickers)
        {
            foreach (string interval in Intervals)
            {
                DateTime from = interval == "1h" ? now.AddDays(-700) : now.AddDays(-58);
                string cell;
                try
                {
                    string json = client.FetchChartJson(ticker, interval, from, now, CancellationToken.None);
                    YahooChartParser.ParseResult p = YahooChartParser.Parse(json);
                    if (p.Bars.Count == 0)
                    {
                        cell = "0 bars (served but empty)";
                    }
                    else
                    {
                        DateTime f = p.Bars[0].Timestamp, l = p.Bars[^1].Timestamp;
                        double span = (l - f).TotalDays;
                        cell = $"{f:yyyy-MM-dd}..{l:yyyy-MM-dd}  ({p.GapCount} gaps)";
                        W(string.Format("{0,-28} {1,-11} {2,-9} {3,-9:0.0} {4}", label, interval, p.Bars.Count, span, cell));
                        anyOk++;
                        continue;
                    }
                }
                catch (YahooProviderException ex) { cell = $"PROVIDER: {ex.Kind} (HTTP {ex.HttpStatusCode?.ToString() ?? "n/a"})"; }
                catch (InvalidOperationException ex) { cell = $"SHAPE/ERROR: {Trim(ex.Message)}"; }
                catch (Exception ex) { cell = $"{ex.GetType().Name}: {Trim(ex.Message)}"; }
                W(string.Format("{0,-28} {1,-11} {2,-9} {3,-9} {4}", label, interval, "-", "-", cell));
            }
        }

        // ── one raw GET to the options endpoint: confirm snapshot-only, no history, no futures options ──
        W("");
        W("Options endpoint (v7/finance/options) - raw GET, snapshot only by design:");
        foreach (string sym in new[] { "SPY", "^SPX", "ES=F", "MES=F" })
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (research-probe)");
                HttpResponseMessage r = http.GetAsync($"https://query2.finance.yahoo.com/v7/finance/options/{Uri.EscapeDataString(sym)}",
                    CancellationToken.None).GetAwaiter().GetResult();
                string body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                bool hasChain = body.Contains("\"expirationDates\"") && body.Contains("\"strikes\"");
                W($"  {sym,-8} HTTP {(int)r.StatusCode}  chain={(hasChain ? "present (current snapshot)" : "absent")}  " +
                  $"len={body.Length}");
            }
            catch (Exception ex) { W($"  {sym,-8} {ex.GetType().Name}: {Trim(ex.Message)}"); }
        }

        W("");
        W($"summary: {anyOk} (ticker,interval) combinations returned usable bars.");

        WriteReport();

        // Not an assertion on production behaviour - only guards against a totally dead network run.
        Assert.SkipUnless(anyOk > 0, "Yahoo unreachable for every ticker/interval this run - re-run when network is available.");
    }

    private static string Trim(string s) => s.Length <= 90 ? s : s[..90] + "...";

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "NewDataSourceFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "yahoo_cross_asset_probe.txt"), string.Join("\n", _log) + "\n");
    }
}
