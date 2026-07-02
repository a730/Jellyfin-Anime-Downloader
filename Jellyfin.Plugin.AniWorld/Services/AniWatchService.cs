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
/// Service for interacting with the aniwatch.one website.
/// Aniwatch.one is an anime streaming site with English sub and dub.
/// </summary>
public class AniWatchService : StreamingSiteService
{
    private static readonly Regex AwTitlePattern = new(
        @"<h2[^>]*class=""film-name""[^>]*><a[^>]*>(?<title>[^<]+)</a>",
        RegexOptions.Compiled);

    private static readonly Regex AwCoverImagePattern = new(
        @"<img[^>]*class=""film-poster-img""[^>]*(?:data-src|src)=""(?<src>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex AwDescriptionPattern = new(
        @"<div[^>]*class=""description""[^>]*>(?<desc>.*?)</div>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AwGenrePattern = new(
        @"<a[^>]*href=""/genre/[^""]+""[^>]*class=""genre-button""[^>]*>(?<genre>[^<]+)</a>",
        RegexOptions.Compiled);

    private static readonly Regex AwSearchResultItem = new(
        @"<div[^>]*class=""flw-item""[^>]*>.*?" +
        @"<img[^>]*(?:data-src|src)=""(?<cover>[^""]+)""[^>]*>.*?" +
        @"<h3[^>]*class=""film-name""[^>]*><a[^>]*href=""(?<url>/watch/[^""]+)""[^>]*>(?<title>[^<]+)</a>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AwEpisodeItem = new(
        @"<a[^>]*href=""(?<url>/watch/[^""]+(?:[?&]ep=|/ep-)(?<num>\d+))""[^>]*>(?:<div[^>]*class=""ss-ep-name""[^>]*>)?(?:\s*Ep\s*)?(?<num2>\d+)\s*(?::?\s*(?<title>[^<]*))?",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AwEpisodeItemAlt = new(
        @"<li[^>]*class=""ep-item""[^>]*>.*?" +
        @"<a[^>]*href=""(?<url>/watch/[^""]+)""[^>]*>.*?" +
        @"(?<num>\d+)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AwProviderPattern = new(
        @"data-link-target=""(?<redirect>[^""]+)""[^>]*data-lang-key=""(?<langKey>[^""]+)""[^>]*>.*?<h4>(?<provider>[^<]+)</h4>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AwEpisodeTitlePattern = new(
        @"<h1[^>]*>(?<title>[^<]+)</h1>",
        RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="AniWatchService"/> class.
    /// </summary>
    public AniWatchService(IHttpClientFactory httpClientFactory, ILogger<AniWatchService> logger)
        : base(httpClientFactory.CreateClient("ANIWATCH"), logger)
    {
    }

    /// <inheritdoc />
    public override string SourceName => "aniwatch";

    /// <inheritdoc />
    protected override string BaseUrl => "https://aniwatch.one";

    /// <inheritdoc />
    protected override string SearchUrl => $"{BaseUrl}/search";

    /// <inheritdoc />
    protected override string SeriesPathPrefix => "/watch/";

    /// <inheritdoc />
    protected override string PopularPath => "/most-popular";

    /// <inheritdoc />
    protected override string NewSectionHeading => "Latest Episodes";

    /// <inheritdoc />
    protected override Regex SeasonLinkPattern => new(@"", RegexOptions.Compiled);

    /// <inheritdoc />
    protected override Regex EpisodeListPattern => new(@"", RegexOptions.Compiled);

    /// <inheritdoc />
    protected override Regex SearchFilterPattern => new(
        @"^/watch/[a-zA-Z0-9\-]+-\d+/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    protected override Regex BrowseItemPattern => AwSearchResultItem;

    /// <inheritdoc />
    public override async Task<List<SearchResult>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var url = $"{SearchUrl}?keyword={Uri.EscapeDataString(keyword)}";
        Logger.LogDebug("AniWatch search: {Url}", url);

        var html = await FetchPageAsync(url, cancellationToken).ConfigureAwait(false);

        var results = new List<SearchResult>();

        foreach (Match match in AwSearchResultItem.Matches(html))
        {
            var link = match.Groups["url"].Value;
            if (!SearchFilterPattern.IsMatch(link))
            {
                continue;
            }

            var title = DecodeHtml(match.Groups["title"].Value.Trim());
            var coverPath = match.Groups["cover"].Value;
            var coverUrl = coverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? coverPath
                : $"{BaseUrl}{coverPath}";

            results.Add(new SearchResult
            {
                Title = title,
                Url = $"{BaseUrl}{link}",
                Description = string.Empty,
                Source = SourceName,
            });
        }

        return results;
    }

    /// <inheritdoc />
    public override async Task<SeriesInfo> GetSeriesInfoAsync(string seriesUrl, CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync(seriesUrl, cancellationToken).ConfigureAwait(false);

        var title = "Unknown";
        var titleMatch = AwTitlePattern.Match(html);
        if (titleMatch.Success)
        {
            title = DecodeHtml(titleMatch.Groups["title"].Value.Trim());
        }
        else
        {
            var h1Match = Regex.Match(html, @"<h1[^>]*>(?<title>[^<]+)</h1>");
            if (h1Match.Success)
            {
                title = DecodeHtml(h1Match.Groups["title"].Value.Trim());
            }
        }

        var coverUrl = string.Empty;
        var posterMatch = Regex.Match(html,
            @"<img[^>]*class=""film-poster-img""[^>]*(?:data-src|src)=""(?<src>(?:https?://)?[^""]+)""");
        if (posterMatch.Success)
        {
            coverUrl = posterMatch.Groups["src"].Value;
            if (!coverUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                coverUrl = $"{BaseUrl}{coverUrl}";
            }
        }
        else
        {
            var imgMatch = Regex.Match(html,
                @"<img[^>]*class=""(?:[^""]*\s)?(?:anime-poster|cover)[^""]*""[^>]*(?:data-src|src)=""(?<src>[^""]+)""");
            if (imgMatch.Success)
            {
                coverUrl = imgMatch.Groups["src"].Value;
                if (!coverUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    coverUrl = $"{BaseUrl}{coverUrl}";
                }
            }
        }

        var description = string.Empty;
        var descMatch = Regex.Match(html,
            @"<div[^>]*class=""description""[^>]*>(?<desc>.*?)</div>",
            RegexOptions.Singleline);
        if (descMatch.Success)
        {
            description = DecodeHtml(Regex.Replace(descMatch.Groups["desc"].Value, "<[^>]+>", string.Empty).Trim());
        }

        var genres = new List<string>();
        var genreMatches = Regex.Matches(html,
            @"<a[^>]*href=""/genre/[^""]+""[^>]*>(?<genre>[^<]+)</a>");
        foreach (Match g in genreMatches)
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

        var epItems = Regex.Matches(html,
            @"<a[^>]*href=""(?<url>/watch/[^""]+)""[^>]*class=""[^""]*ss-ep-btn[^""]*""[^>]*>(?:\s*Ep\s*)?(?<num>\d+)",
            RegexOptions.Singleline);

        if (epItems.Count == 0)
        {
            epItems = Regex.Matches(html,
                @"<li[^>]*class=""ep-item""[^>]*>.*?<a[^>]*href=""(?<url>/watch/[^""]+)""[^>]*>.*?(?:Ep\s*)?(?<num>\d+)",
                RegexOptions.Singleline);
        }

        if (epItems.Count == 0)
        {
            epItems = Regex.Matches(html,
                @"<a[^>]*href=""(?<url>/watch/[^""]+/ep-(?<num>\d+))""",
                RegexOptions.Singleline);
        }

        foreach (Match match in epItems)
        {
            var url = match.Groups["url"].Value;
            if (!episodeUrls.Add(url))
            {
                continue;
            }

            var numStr = match.Groups["num"].Value;
            if (!int.TryParse(numStr, out var num))
            {
                num = episodes.Count + 1;
            }

            episodes.Add(new EpisodeRef
            {
                Url = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"{BaseUrl}{url}",
                Number = num,
                IsMovie = false,
            });
        }

        return episodes.OrderBy(e => e.Number).ToList();
    }

    /// <inheritdoc />
    public override async Task<EpisodeDetails> GetEpisodeDetailsAsync(string episodeUrl, CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync(episodeUrl, cancellationToken).ConfigureAwait(false);

        var titleEn = string.Empty;
        var titleDe = string.Empty;

        var h1Match = Regex.Match(html, @"<h1[^>]*>(?<title>[^<]+)</h1>");
        if (h1Match.Success)
        {
            titleEn = DecodeHtml(h1Match.Groups["title"].Value.Trim());
        }

        var providers = new Dictionary<string, Dictionary<string, string>>();

        var liPattern = new Regex(
            @"<li[^>]*data-lang-key=""(?<langKey>\d+)""[^>]*data-link-target=""(?<redirect>[^""]+)""[^>]*>.*?<h4>(?<provider>[^<]+)</h4>",
            RegexOptions.Singleline);

        foreach (Match match in liPattern.Matches(html))
        {
            var langKey = match.Groups["langKey"].Value;
            var redirect = match.Groups["redirect"].Value;
            var provider = match.Groups["provider"].Value.Trim();

            if (!providers.ContainsKey(langKey))
            {
                providers[langKey] = new Dictionary<string, string>();
            }

            var redirectUrl = redirect.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? redirect
                : $"{BaseUrl}{redirect}";

            providers[langKey][provider] = redirectUrl;
        }

        if (providers.Count == 0)
        {
            var dataPattern = new Regex(
                @"data-server-id=""(?<server>[^""]+)""[^>]*data-ep-id=""(?<ep>[^""]+)""[^>]*data-quality=""(?<quality>[^""]+)""",
                RegexOptions.Singleline);

            foreach (Match match in dataPattern.Matches(html))
            {
                var langKey = "2";
                var server = match.Groups["server"].Value;
                var ep = match.Groups["ep"].Value;
                var quality = match.Groups["quality"].Value;

                var providerName = server switch
                {
                    "1" => "VOE",
                    "2" => "Vidmoly",
                    "3" => "Filemoon",
                    "4" => "Vidoza",
                    _ => $"Server-{server}",
                };

                var redirectUrl = $"{BaseUrl}/ajax/ep/info?ep={ep}&server={server}";

                if (!providers.ContainsKey(langKey))
                {
                    providers[langKey] = new Dictionary<string, string>();
                }

                providers[langKey][providerName] = redirectUrl;
            }
        }

        return new EpisodeDetails
        {
            Url = episodeUrl,
            TitleDe = string.IsNullOrEmpty(titleDe) ? null : titleDe,
            TitleEn = string.IsNullOrEmpty(titleEn) ? null : titleEn,
            ProvidersByLanguage = providers,
        };
    }

    /// <inheritdoc />
    public override async Task<List<BrowseItem>> GetPopularAsync(CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync($"{BaseUrl}{PopularPath}", cancellationToken).ConfigureAwait(false);
        return ParseAwBrowseItems(html);
    }

    /// <inheritdoc />
    public override async Task<List<BrowseItem>> GetNewReleasesAsync(CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync($"{BaseUrl}/home", cancellationToken).ConfigureAwait(false);
        return ParseAwBrowseItems(html);
    }

    private List<BrowseItem> ParseAwBrowseItems(string html)
    {
        var items = new List<BrowseItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var itemPattern = new Regex(
            @"<div[^>]*class=""flw-item""[^>]*>.*?" +
            @"<img[^>]*(?:data-src|src)=""(?<cover>[^""]+)""[^>]*>.*?" +
            @"<h3[^>]*class=""film-name""[^>]*><a[^>]*href=""(?<url>/watch/[^""]+)""[^>]*>(?<name>[^<]+)</a>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        foreach (Match match in itemPattern.Matches(html))
        {
            var url = match.Groups["url"].Value;
            if (!seen.Add(url))
            {
                continue;
            }

            var coverPath = match.Groups["cover"].Value;
            var coverUrl = coverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? coverPath
                : $"{BaseUrl}{coverPath}";

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
}
