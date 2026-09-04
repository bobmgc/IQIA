using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace IQIAIndicator.Tests.Research.OrderFlowFeasibility;

/// <summary>
/// QDE-016 — READ-ONLY reflection probe. Dumps the ATAS SDK order-flow surface (IndicatorCandle,
/// the Indicator base class, ATAS.DataFeedsCore market-data types) so the QDE-016 report can scope the
/// order-flow collector on VERIFIED type members rather than assumptions. Modifies no production type.
/// This probe CANNOT answer "are there real numbers at J-30 on a loaded chart" — that requires a
/// connected ATAS chart (see the report's Phase 0). It only establishes what the API exposes.
/// </summary>
public sealed class AtasOrderFlowSurfaceProbeTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public AtasOrderFlowSurfaceProbeTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    [Fact]
    public void Dump_ATAS_OrderFlow_Type_Surface()
    {
        Assembly indicators = typeof(ATAS.Indicators.Indicator).Assembly;
        Assembly feeds = typeof(ATAS.DataFeedsCore.Security).Assembly;
        W($"ATAS.Indicators: {indicators.GetName().Name} v{indicators.GetName().Version} @ {indicators.Location}");
        W($"ATAS.DataFeedsCore: {feeds.GetName().Name} v{feeds.GetName().Version}");
        W("");

        // ── IndicatorCandle : the per-bar order-flow container ──────────────────────────────────
        Type? candle = indicators.GetType("ATAS.Indicators.IndicatorCandle");
        DumpType("ATAS.Indicators.IndicatorCandle", candle, membersOfInterest:
            new[] { "Open", "High", "Low", "Close", "Volume", "Time", "LastTime", "Ticks",
                    "Bid", "Ask", "Delta", "MaxDelta", "MinDelta", "MinMaxDelta",
                    "OI", "MaxOI", "MinOI", "Direction",
                    "GetAllPriceLevels", "GetPriceVolumeInfo", "GetKeyPriceLevels", "GetPriceRange",
                    "PriceLevels", "MaxVolumePriceInfo", "MaxTickPriceInfo" });

        // ── PriceVolumeInfo : the footprint cell ───────────────────────────────────────────────
        DumpType("ATAS.Indicators.PriceVolumeInfo", indicators.GetType("ATAS.Indicators.PriceVolumeInfo"), null);

        // ── Indicator base : DOM / trade / market-data hooks ───────────────────────────────────
        DumpMembersMatching(typeof(ATAS.Indicators.Indicator), "ATAS.Indicators.Indicator",
            new[] { "MarketDepth", "Depth", "BestBid", "BestAsk", "OnNewTrade", "OnBestBid",
                    "MarketDataArg", "Trade", "Cumulative", "Security", "InstrumentInfo",
                    "OnCalculate", "GetCandle", "Subscribe", "Request" });

        // ── ATAS.DataFeedsCore : historical trade / market-depth request APIs ──────────────────
        W("");
        W("=== ATAS.DataFeedsCore types mentioning Trade / Depth / History / Request / MarketByOrder ===");
        foreach (Type t in SafeTypes(feeds)
                     .Where(t => t.IsPublic && Regex(t.Name, "Trade", "Depth", "History", "Request", "MarketByOrder", "Tick", "Candle"))
                     .OrderBy(t => t.FullName))
        {
            W($"  {t.FullName}  ({(t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : "class")})");
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && Regex(m.Name, "Trade", "Histor", "Request", "Load", "Get", "Subscribe")))
                W($"      {Sig(m)}");
        }

        // ── ATAS.Indicators : same scan ───────────────────────────────────────────────────────
        W("");
        W("=== ATAS.Indicators types mentioning MarketDepth / Trade / OrderFlow / Footprint ===");
        foreach (Type t in SafeTypes(indicators)
                     .Where(t => t.IsPublic && Regex(t.Name, "MarketDepth", "Trade", "OrderFlow", "Footprint", "PriceVolume", "Cluster"))
                     .OrderBy(t => t.FullName))
            W($"  {t.FullName}");

        WriteReport();
        Assert.NotNull(candle);
    }

    private void DumpType(string label, Type? t, string[]? membersOfInterest)
    {
        W($"=== {label} ===");
        if (t is null) { W("  <type not found in this SDK version>"); W(""); return; }
        W($"  kind: {(t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : "class")}, " +
          $"base: {t.BaseType?.FullName}");
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
            W($"  prop  {TypeName(p.PropertyType),-32} {p.Name}  {{ {(p.CanRead ? "get; " : "")}{(p.CanWrite ? "set; " : "")}}}");
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name))
            W($"  field {TypeName(f.FieldType),-32} {f.Name}");
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
            W($"  method {Sig(m)}");
        if (membersOfInterest is not null)
        {
            var have = new HashSet<string>(t.GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name));
            string present = string.Join(", ", membersOfInterest.Where(have.Contains));
            string absent = string.Join(", ", membersOfInterest.Where(x => !have.Contains(x)));
            W($"  >> of interest PRESENT: {present}");
            W($"  >> of interest ABSENT : {absent}");
        }
        W("");
    }

    private void DumpMembersMatching(Type t, string label, string[] needles)
    {
        W($"=== {label} — members matching {{{string.Join(", ", needles)}}} ===");
        foreach (MemberInfo mi in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                     .Where(mi => Regex(mi.Name, needles)).OrderBy(mi => mi.Name).DistinctBy(mi => mi.Name))
        {
            string kind = mi.MemberType.ToString().ToLowerInvariant();
            string extra = mi switch
            {
                PropertyInfo p => $"{TypeName(p.PropertyType)}",
                MethodInfo m => Sig(m),
                EventInfo e => $"event {TypeName(e.EventHandlerType!)}",
                FieldInfo f => $"{TypeName(f.FieldType)}",
                _ => ""
            };
            string vis = mi switch
            {
                MethodInfo m => m.IsPublic ? "public" : m.IsFamily ? "protected" : "private",
                PropertyInfo p => (p.GetMethod?.IsPublic ?? false) ? "public" : (p.GetMethod?.IsFamily ?? false) ? "protected" : "private",
                _ => ""
            };
            W($"  {vis,-9} {kind,-8} {mi.Name}  :: {extra}");
        }
        W("");
    }

    private static bool Regex(string s, params string[] needles) =>
        needles.Any(n => s.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static string Sig(MethodInfo m) =>
        $"{TypeName(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{TypeName(p.ParameterType)} {p.Name}"))})";

    private static string TypeName(Type t)
    {
        if (t.IsGenericType)
            return $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(TypeName))}>";
        return t.Name;
    }

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "OrderFlowFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "atas_orderflow_surface.txt"), string.Join("\n", _log) + "\n");
    }
}
