using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using HtmlAgilityPack;

namespace Echokraut.Services;

/// <summary>
/// Lodestone-backed (HTML-scraping) lookup with DB cache and a 500ms throttle.
/// Misses are negatively cached so unknown players don't keep hitting the network.
/// Direct HTTP + HtmlAgilityPack — NetStone's API was too thin (no Race/Gender exposure).
/// </summary>
public class LodestoneService : ILodestoneService
{
    private const string LodestoneBase = "https://eu.finalfantasyxiv.com";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan MinRequestGap = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogService _log;
    private readonly IDatabaseService _db;
    private readonly SemaphoreSlim _throttleLock = new(1, 1);
    private DateTime _lastRequest = DateTime.MinValue;

    private readonly HttpClient _http;

    public LodestoneService(ILogService log, IDatabaseService db)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _db = db ?? throw new ArgumentNullException(nameof(db));

        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            Timeout = RequestTimeout,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; Echokraut/1.0; +https://github.com/RenNagasaki/Echokraut)");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
    }

    public async Task<LodestoneResult?> LookupAsync(string name, string world, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
            return null;

        // 1) Cache check
        var cached = _db.GetLodestoneLookup(name, world);
        if (cached != null && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
        {
            if (!cached.Found) return null;
            var raceCached = (NpcRaces)cached.Race;
            return new LodestoneResult(raceCached, raceCached.ToString(), (Genders)cached.Gender);
        }

        // 2) Throttled live fetch
        await _throttleLock.WaitAsync(ct);
        try
        {
            await ApplyThrottleAsync(ct);

            var characterId = await SearchCharacterIdAsync(name, world, ct);
            if (characterId == null)
            {
                _db.UpsertLodestoneLookup(name, world, NpcRaces.Unknown, Genders.None, found: false);
                _log.Info(nameof(LookupAsync), $"Lodestone miss (search): {name} @ {world}",
                    new EKEventId(0, TextSource.None));
                return null;
            }

            await ApplyThrottleAsync(ct);
            var profile = await FetchCharacterProfileAsync(characterId, ct);
            if (profile == null)
            {
                _db.UpsertLodestoneLookup(name, world, NpcRaces.Unknown, Genders.None, found: false);
                _log.Info(nameof(LookupAsync), $"Lodestone miss (profile): {name} @ {world} (id={characterId})",
                    new EKEventId(0, TextSource.None));
                return null;
            }

            var (race, gender) = profile.Value;
            _db.UpsertLodestoneLookup(name, world, race, gender, found: true);
            _log.Info(nameof(LookupAsync),
                $"Lodestone hit: {name} @ {world} → {race} {gender} (id={characterId})",
                new EKEventId(0, TextSource.None));
            return new LodestoneResult(race, race.ToString(), gender);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LookupAsync), $"Lodestone error for {name} @ {world}: {ex.Message}",
                new EKEventId(0, TextSource.None));
            try { _db.UpsertLodestoneLookup(name, world, NpcRaces.Unknown, Genders.None, found: false); } catch { }
            return null;
        }
        finally
        {
            _throttleLock.Release();
        }
    }

    private async Task ApplyThrottleAsync(CancellationToken ct)
    {
        var elapsed = DateTime.UtcNow - _lastRequest;
        if (elapsed < MinRequestGap)
            await Task.Delay(MinRequestGap - elapsed, ct);
        _lastRequest = DateTime.UtcNow;
    }

    private async Task<string?> SearchCharacterIdAsync(string name, string world, CancellationToken ct)
    {
        var url = $"{LodestoneBase}/lodestone/character/?q={HttpUtility.UrlEncode(name)}&worldname={HttpUtility.UrlEncode(world)}";
        var html = await GetHtmlAsync(url, ct);
        if (html == null) return null;

        // Search results: <a href="/lodestone/character/{id}/" class="entry__link">…<p class="entry__name">{name}</p>…<p class="entry__world">…{world}…</p>
        var entries = html.DocumentNode.SelectNodes("//a[contains(@class,'entry__link') and contains(@href,'/lodestone/character/')]");
        if (entries == null) return null;

        foreach (var entry in entries)
        {
            // HtmlAgilityPack's InnerText does NOT decode entities — names with apostrophes ("G'juhrih Ami")
            // come back as "G&#39;juhrih Ami". HttpUtility.HtmlDecode normalizes them.
            var entryName = HttpUtility.HtmlDecode(
                entry.SelectSingleNode(".//p[contains(@class,'entry__name')]")?.InnerText ?? "").Trim();
            var entryWorld = HttpUtility.HtmlDecode(
                entry.SelectSingleNode(".//p[contains(@class,'entry__world')]")?.InnerText ?? "").Trim();
            if (string.IsNullOrEmpty(entryName)) continue;
            if (!string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(entryWorld) && !entryWorld.Contains(world, StringComparison.OrdinalIgnoreCase)) continue;

            var href = entry.GetAttributeValue("href", "");
            // /lodestone/character/12345678/
            var match = System.Text.RegularExpressions.Regex.Match(href, @"/lodestone/character/(\d+)/");
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }

    private async Task<(NpcRaces race, Genders gender)?> FetchCharacterProfileAsync(string characterId, CancellationToken ct)
    {
        var url = $"{LodestoneBase}/lodestone/character/{characterId}/";
        var html = await GetHtmlAsync(url, ct);
        if (html == null) return null;

        // Race/Clan/Gender appears in:
        //   <p class="character-block__name">Hyur<br>Midlander / ♂</p>
        // The first character-block__name on the profile holds race+clan+gender.
        var raceBlock = html.DocumentNode.SelectSingleNode("//p[contains(@class,'character-block__name')]");
        if (raceBlock == null) return null;

        var raw = HttpUtility.HtmlDecode(raceBlock.InnerHtml ?? "");
        // Split on <br> in any form
        var parts = System.Text.RegularExpressions.Regex.Split(raw, @"<br\s*/?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (parts.Length < 2) return null;

        var raceText = StripHtml(parts[0]).Trim();
        var clanGender = StripHtml(parts[1]).Trim(); // "Midlander / ♂"

        var race = ParseRace(raceText);
        var gender = ParseGender(clanGender);
        return (race, gender);
    }

    private async Task<HtmlDocument?> GetHtmlAsync(string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(body);
            return doc;
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(GetHtmlAsync), $"HTTP GET failed for {url}: {ex.Message}",
                new EKEventId(0, TextSource.None));
            return null;
        }
    }

    private static string StripHtml(string s)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(s);
        return doc.DocumentNode.InnerText;
    }

    private static NpcRaces ParseRace(string race) => race?.Trim().ToLowerInvariant() switch
    {
        "hyur" => NpcRaces.Hyur,
        "elezen" => NpcRaces.Elezen,
        "lalafell" => NpcRaces.Lalafell,
        "miqo'te" => NpcRaces.Miqote,
        "miqote" => NpcRaces.Miqote,
        "roegadyn" => NpcRaces.Roegadyn,
        "au ra" => NpcRaces.AuRa,
        "aura" => NpcRaces.AuRa,
        "hrothgar" => NpcRaces.Hrothgar,
        "viera" => NpcRaces.Viera,
        _ => NpcRaces.Unknown,
    };

    private static Genders ParseGender(string clanGender)
    {
        if (string.IsNullOrEmpty(clanGender)) return Genders.None;
        if (clanGender.Contains('♂')) return Genders.Male;
        if (clanGender.Contains('♀')) return Genders.Female;
        // fallback: word-based for non-symbol locales
        if (clanGender.Contains("male", StringComparison.OrdinalIgnoreCase) &&
            !clanGender.Contains("female", StringComparison.OrdinalIgnoreCase))
            return Genders.Male;
        if (clanGender.Contains("female", StringComparison.OrdinalIgnoreCase))
            return Genders.Female;
        return Genders.None;
    }
}
