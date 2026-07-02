using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniWorld.Services;

/// <summary>
/// Service for interacting with the animex.one website.
/// Animex.one is an anime streaming site using SvelteKit with AniList data.
/// </summary>
public class AnimeXService : StreamingSiteService
{
    private static readonly Regex AxTitlePattern = new(
        @"<h1[^>]*>(?<title>[^<]+)</h1>",
        RegexOptions.Compiled);

    private static readonly Regex AxDescriptionPattern = new(
        @"<p[^>]*class=""description""[^>]*>(?<desc>.*?)</p>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxGenrePattern = new(
        @"<a[^>]*href=""/anime[^""]*\?genres?=[^""]+""[^>]*>(?<genre>[^<]+)</a>",
        RegexOptions.Compiled);

    private static readonly Regex AxCoverPattern = new(
        @"<img[^>]*class=""cover""[^>]*src=""(?<src>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex AxCoverAltPattern = new(
        @"<meta[^>]*property=""og:image""[^>]*content=""(?<src>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex AxSearchItemAlt = new(
        @"<div[^>]*class=""anime-card""[^>]*>.*?" +
        @"<a[^>]*href=""(?<url>/anime/[^""]+)""[^>]*>.*?" +
        @"<img[^>]*src=""(?<cover>[^""]+)""[^>]*>.*?" +
        @"<h3[^>]*>(?<name>[^<]+)</h3>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxSearchItemAlt2 = new(
        @"<a[^>]*href=""(?<url>/anime/[^""]+)""[^>]*>.*?" +
        @"<img[^>]*(?:src|data-src)=""(?<cover>[^""]+)""[^>]*>.*?" +
        @"<span[^>]*class=""[^""]*title[^""]*""[^>]*>(?<name>[^<]+)</span>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxSearchResultFromLi = new(
        @"<li[^>]*>.*?<a[^>]*href=""(?<url>/anime/[^""]+)""[^>]*>(?<title>[^<]+)</a>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxJsonLd = new(
        @"<script[^>]*type=""application/ld\+json""[^>]*>(?<json>.*?)</script>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxEpisodeItem = new(
        @"<a[^>]*href=""(?<url>/anime/[^""]+/(?:episode|watch|ep)/(?<num>\d+))""[^>]*>(?:\s*Ep\s*#?\s*)?(?<num2>\d+)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxEpisodeCountMeta = new(
        @"<p[^>]*class=""episode-count""[^>]*>.*?(?<count>\d+)\s*(?:episodes?|eps?)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxEpisodeFromScript = new(
        @"episodes\s*:\s*\[(?<eps>.*?)\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxEpisodeEpisodePattern = new(
        @"""episode""\s*:\s*(?<num>\d+)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly string[] AxImageCdnDomains =
    {
        "serveproxy.com",
        "img.animex.one",
        "cdn.animex.one",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeXService"/> class.
    /// </summary>
    public AnimeXService(IHttpClientFactory httpClientFactory, ILogger<AnimeXService> logger)
        : base(httpClientFactory.CreateClient("ANIMEX"), logger)
    {
    }

    /// <inheritdoc />
    public override string SourceName => "animex";

    /// <inheritdoc />
    protected override string BaseUrl => "https://animex.one";

    /// <inheritdoc />
    protected override string SearchUrl => $"{BaseUrl}/catalog";

    /// <inheritdoc />
    protected override string SeriesPathPrefix => "/anime/";

    /// <inheritdoc />
    protected override string PopularPath => "/trending";

    /// <inheritdoc />
    protected override string NewSectionHeading => "Latest Episodes";

    /// <inheritdoc />
    protected override Regex SeasonLinkPattern => new(@"", RegexOptions.Compiled);

    /// <inheritdoc />
    protected override Regex EpisodeListPattern => new(@"", RegexOptions.Compiled);

    /// <inheritdoc />
    protected override Regex SearchFilterPattern => new(
        @"^/anime/[a-zA-Z0-9\-]+-\d+/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    protected override Regex BrowseItemPattern => AxSearchItemAlt;

    /// <inheritdoc />
    public override async Task<List<SearchResult>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var url = $"{SearchUrl}?search={Uri.EscapeDataString(keyword)}";
        Logger.LogDebug("AnimeX search: {Url}", url);

        var html = await FetchPageAsync(url, cancellationToken).ConfigureAwait(false);

        var results = new List<SearchResult>();

        foreach (Match match in AxSearchItemAlt.Matches(html))
        {
            var link = match.Groups["url"].Value;
            if (!SearchFilterPattern.IsMatch(link))
            {
                continue;
            }

            var title = DecodeHtml(match.Groups["name"].Value.Trim());
            var coverPath = match.Groups["cover"].Value;
            var coverUrl = NormalizeImageUrl(coverPath);

            results.Add(new SearchResult
            {
                Title = title,
                Url = $"{BaseUrl}{link}",
                Description = string.Empty,
                Source = SourceName,
            });
        }

        if (results.Count == 0)
        {
            foreach (Match match in AxSearchItemAlt2.Matches(html))
            {
                var link = match.Groups["url"].Value;
                if (!SearchFilterPattern.IsMatch(link))
                {
                    continue;
                }

                var title = DecodeHtml(match.Groups["name"].Value.Trim());
                var coverPath = match.Groups["cover"].Value;
                var coverUrl = NormalizeImageUrl(coverPath);

                results.Add(new SearchResult
                {
                    Title = title,
                    Url = $"{BaseUrl}{link}",
                    Description = string.Empty,
                    Source = SourceName,
                });
            }
        }

        if (results.Count == 0)
        {
            foreach (Match match in AxSearchResultFromLi.Matches(html))
            {
                var link = match.Groups["url"].Value;
                if (!SearchFilterPattern.IsMatch(link))
                {
                    continue;
                }

                var title = DecodeHtml(match.Groups["title"].Value.Trim());

                results.Add(new SearchResult
                {
                    Title = title,
                    Url = $"{BaseUrl}{link}",
                    Description = string.Empty,
                    Source = SourceName,
                });
            }
        }

        if (results.Count == 0)
        {
            Logger.LogWarning("AnimeX search returned no results. HTML length: {Len}", html.Length);
            Logger.LogDebug("AnimeX search HTML (first 1000 chars): {Html}", html?.Substring(0, Math.Min(1000, html?.Length ?? 0)));
        }

        return results;
    }

    /// <inheritdoc />
    public override async Task<SeriesInfo> GetSeriesInfoAsync(string seriesUrl, CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync(seriesUrl, cancellationToken).ConfigureAwait(false);

        var title = "Unknown";
        var titleMatch = AxTitlePattern.Match(html);
        if (titleMatch.Success)
        {
            title = DecodeHtml(titleMatch.Groups["title"].Value.Trim());
        }

        var coverUrl = string.Empty;
        var coverMatch = AxCoverPattern.Match(html);
        if (coverMatch.Success)
        {
            coverUrl = NormalizeImageUrl(coverMatch.Groups["src"].Value);
        }
        else
        {
            var coverAltMatch = AxCoverAltPattern.Match(html);
            if (coverAltMatch.Success)
            {
                coverUrl = NormalizeImageUrl(coverAltMatch.Groups["src"].Value);
            }
        }

        var jsonLdMatch = AxJsonLd.Match(html);
        if (jsonLdMatch.Success && string.IsNullOrEmpty(coverUrl))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonLdMatch.Groups["json"].Value);
                if (doc.RootElement.TryGetProperty("image", out var imgProp))
                {
                    coverUrl = imgProp.GetString() ?? string.Empty;
                }

                if (string.IsNullOrEmpty(title) || title == "Unknown")
                {
                    if (doc.RootElement.TryGetProperty("name", out var nameProp))
                    {
                        title = nameProp.GetString() ?? "Unknown";
                    }
                }
            }
            catch (JsonException ex)
            {
                Logger.LogDebug(ex, "Failed to parse JSON-LD from AnimeX page");
            }
        }

        var description = string.Empty;
        var descMatch = AxDescriptionPattern.Match(html);
        if (descMatch.Success)
        {
            description = DecodeHtml(Regex.Replace(descMatch.Groups["desc"].Value, "<[^>]+>", string.Empty).Trim());
        }

        if (string.IsNullOrEmpty(description))
        {
            var metaDescMatch = Regex.Match(html,
                @"<meta[^>]*name=""description""[^>]*content=""(?<desc>[^""]+)""");
            if (metaDescMatch.Success)
            {
                description = DecodeHtml(metaDescMatch.Groups["desc"].Value.Trim());
            }
        }

        var genres = new List<string>();
        foreach (Match g in AxGenrePattern.Matches(html))
        {
            var genre = DecodeHtml(g.Groups["genre"].Value.Trim());
            if (!string.IsNullOrEmpty(genre))
            {
                genres.Add(genre);
            }
        }

        genres = genres.Distinct().ToList();

        return new SeriesInfo
        {
            Title = title,
            Url = seriesUrl,
            CoverImageUrl = coverUrl,
            Description = description,
            Genres = genres,
            Seasons = new List<SeasonRef>(),
            HasMovies = false,
        };
    }

    /// <inheritdoc />
    public override async Task<List<EpisodeRef>> GetEpisodesAsync(string seasonUrl, CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync(seasonUrl, cancellationToken).ConfigureAwait(false);

        var episodes = new List<EpisodeRef>();
        var episodeUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AxEpisodeItem.Matches(html))
        {
            var url = match.Groups["url"].Value;
            if (!episodeUrls.Add(url))
            {
                continue;
            }

            var numStr = match.Groups["num"].Value;
            if (!int.TryParse(numStr, out var num))
            {
                if (!int.TryParse(match.Groups["num2"].Value, out num))
                {
                    continue;
                }
            }

            episodes.Add(new EpisodeRef
            {
                Url = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"{BaseUrl}{url}",
                Number = num,
                IsMovie = false,
            });
        }

        if (episodes.Count == 0)
        {
            var scriptEps = AxEpisodeFromScript.Match(html);
            if (scriptEps.Success)
            {
                var epJson = $"[{scriptEps.Groups["eps"].Value}]";
                try
                {
                    using var doc = JsonDocument.Parse(epJson);
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (el.TryGetProperty("episode", out var epProp) && epProp.TryGetInt32(out var num))
                        {
                            episodes.Add(new EpisodeRef
                            {
                                Url = $"{seasonUrl}/episode/{num}",
                                Number = num,
                                IsMovie = false,
                            });
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Logger.LogDebug(ex, "Failed to parse inline episode JSON from AnimeX");
                }
            }
        }

        return episodes.OrderBy(e => e.Number).ToList();
    }

    /// <inheritdoc />
    public override async Task<EpisodeDetails> GetEpisodeDetailsAsync(string episodeUrl, CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync(episodeUrl, cancellationToken).ConfigureAwait(false);

        var titleEn = string.Empty;
        var h1Match = Regex.Match(html, @"<h1[^>]*>(?<title>[^<]+)</h1>");
        if (h1Match.Success)
        {
            titleEn = DecodeHtml(h1Match.Groups["title"].Value.Trim());
        }

        var providers = new Dictionary<string, Dictionary<string, string>>();

        var serverPattern = new Regex(
            @"(?:data-server|data-provider|data-source)""?\s*[:=]\s*""?(?<server>[^""'/>]+)""?\s*[},]",
            RegexOptions.Singleline | RegexOptions.Compiled);

        foreach (Match match in serverPattern.Matches(html))
        {
            var langKey = "2";
            var server = match.Groups["server"].Value.Trim();

            if (!providers.ContainsKey(langKey))
            {
                providers[langKey] = new Dictionary<string, string>();
            }

            var providerName = server switch
            {
                "voe" => "VOE",
                "vidmoly" => "Vidmoly",
                "filemoon" => "Filemoon",
                "vidoza" => "Vidoza",
                _ => server,
            };

            if (!providers[langKey].ContainsKey(providerName))
            {
                providers[langKey][providerName] = episodeUrl;
            }
        }

        if (providers.Count == 0)
        {
            var anchorPattern = new Regex(
                @"<a[^>]*href=""(?<url>[^""]*)(?:voe|vidmoly|filemoon|vidoza)[^""]*""[^>]*>(?<name>[^<]*)</a>",
                RegexOptions.Singleline | RegexOptions.Compiled);

            foreach (Match match in anchorPattern.Matches(html))
            {
                var langKey = "2";
                var providerName = DecodeHtml(match.Groups["name"].Value.Trim());
                if (string.IsNullOrEmpty(providerName))
                {
                    providerName = match.Groups["url"].Value.Contains("voe", StringComparison.OrdinalIgnoreCase) ? "VOE" :
                        match.Groups["url"].Value.Contains("vidmoly", StringComparison.OrdinalIgnoreCase) ? "Vidmoly" :
                        match.Groups["url"].Value.Contains("filemoon", StringComparison.OrdinalIgnoreCase) ? "Filemoon" :
                        match.Groups["url"].Value.Contains("vidoza", StringComparison.OrdinalIgnoreCase) ? "Vidoza" :
                        "Unknown";
                }

                if (!providers.ContainsKey(langKey))
                {
                    providers[langKey] = new Dictionary<string, string>();
                }

                providers[langKey][providerName] = match.Groups["url"].Value;
            }
        }

        return new EpisodeDetails
        {
            Url = episodeUrl,
            TitleDe = null,
            TitleEn = string.IsNullOrEmpty(titleEn) ? null : titleEn,
            ProvidersByLanguage = providers,
        };
    }

    /// <inheritdoc />
    public override async Task<List<BrowseItem>> GetPopularAsync(CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync($"{BaseUrl}{PopularPath}", cancellationToken).ConfigureAwait(false);
        return ParseAxBrowseItems(html);
    }

    /// <inheritdoc />
    public override async Task<List<BrowseItem>> GetNewReleasesAsync(CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync($"{BaseUrl}/", cancellationToken).ConfigureAwait(false);
        return ParseAxBrowseItems(html);
    }

    private List<BrowseItem> ParseAxBrowseItems(string html)
    {
        var items = new List<BrowseItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AxSearchItemAlt.Matches(html))
        {
            var url = match.Groups["url"].Value;
            if (!seen.Add(url))
            {
                continue;
            }

            var coverPath = match.Groups["cover"].Value;
            var coverUrl = NormalizeImageUrl(coverPath);
            var title = DecodeHtml(match.Groups["name"].Value.Trim());

            items.Add(new BrowseItem
            {
                Title = title,
                Url = $"{BaseUrl}{url}",
                CoverImageUrl = coverUrl,
                Genre = string.Empty,
                Source = SourceName,
            });
        }

        return items;
    }

    private string NormalizeImageUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url.StartsWith("//", StringComparison.OrdinalIgnoreCase))
        {
            return $"https:{url}";
        }

        return $"{BaseUrl}{url}";
    }
}
