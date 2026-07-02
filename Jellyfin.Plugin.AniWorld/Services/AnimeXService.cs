using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniWorld.Services;

public class AnimeXService : StreamingSiteService
{
    private const string GraphQlApi = "https://graphql.animex.one";
    private const string FastSearchQuery = @"
        query FastSearch($query: String, $limit: Int, $includeAdult: Boolean) {
            catalogAnime(filter: { query: $query, includeAdult: $includeAdult }, limit: $limit) {
                items {
                    id
                    anilistId
                    malId
                    titleRomaji
                    titleEnglish
                    coverImage
                    format
                    status
                    episodeCount
                    seasonYear
                    season
                    color
                    genres
                    bannerImage
                }
            }
        }";

    private const string CatalogQuery = @"
        query CatalogAnime($filter: AnimeCatalogFilterInput, $sort: [AnimeSortInput!], $limit: Int, $offset: Int) {
            catalogAnime(filter: $filter, sort: $sort, limit: $limit, offset: $offset) {
                items {
                    id
                    anilistId
                    malId
                    titleRomaji
                    titleEnglish
                    coverImage
                    bannerImage
                    backdropUrl
                    description
                    trailerId
                    status
                    format
                    averageScore
                    popularity
                    nextAiringAt
                    nextAiringEpisode
                    episodeCount
                    seasonYear
                    season
                    color
                    genres
                    subCount
                    dubCount
                }
                totalCount
                limit
                offset
                currentPage
                totalPages
                hasNextPage
                hasPreviousPage
            }
        }";

    private static readonly Regex AxTitlePattern = new(
        @"<h1[^>]*>(?<title>[^<]+)</h1>",
        RegexOptions.Compiled);

    private static readonly Regex AxDescriptionPattern = new(
        @"<p[^>]*class=""description""[^>]*>(?<desc>.*?)</p>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxCoverPattern = new(
        @"<img[^>]*class=""cover""[^>]*src=""(?<src>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex AxCoverAltPattern = new(
        @"<meta[^>]*property=""og:image""[^>]*content=""(?<src>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex AxJsonLd = new(
        @"<script[^>]*type=""application/ld\+json""[^>]*>(?<json>.*?)</script>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxEpisodeItem = new(
        @"<a[^>]*href=""(?<url>/anime/[^""]+/(?:episode|watch|ep)/(?<num>\d+))""[^>]*>(?:\s*Ep\s*#?\s*)?(?<num2>\d+)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AxEpisodeFromScript = new(
        @"episodes\s*:\s*\[(?<eps>.*?)\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public AnimeXService(IHttpClientFactory httpClientFactory, ILogger<AnimeXService> logger)
        : base(httpClientFactory.CreateClient("ANIMEX"), logger)
    {
    }

    public override string SourceName => "animex";

    protected override string BaseUrl => "https://animex.one";

    protected override string SearchUrl => $"{BaseUrl}/catalog";

    protected override string SeriesPathPrefix => "/anime/";

    protected override string PopularPath => "/trending";

    protected override string NewSectionHeading => "Latest Episodes";

    protected override Regex SeasonLinkPattern => new(@"", RegexOptions.Compiled);

    protected override Regex EpisodeListPattern => new(@"", RegexOptions.Compiled);

    protected override Regex SearchFilterPattern => new(
        @"^/anime/[a-zA-Z0-9\-]+/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    protected override Regex BrowseItemPattern => new(@"", RegexOptions.Compiled);

    public override async Task<List<SearchResult>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("AnimeX GraphQL search: {Keyword}", keyword);

        var payload = new
        {
            query = FastSearchQuery,
            variables = new { query = keyword, limit = 20, includeAdult = false }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await HttpClient.PostAsync($"{GraphQlApi}/graphql", content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);

            var items = doc.RootElement.GetProperty("data").GetProperty("catalogAnime").GetProperty("items");

            var results = new List<SearchResult>();
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? string.Empty;
                var titleRomaji = item.GetProperty("titleRomaji").GetString() ?? string.Empty;
                var titleEnglish = item.TryGetProperty("titleEnglish", out var te) ? te.GetString() ?? string.Empty : string.Empty;
                var title = string.IsNullOrEmpty(titleEnglish) ? titleRomaji : titleEnglish;

                string coverUrl = string.Empty;
                if (item.TryGetProperty("coverImage", out var cover) && cover.ValueKind == JsonValueKind.Object)
                {
                    if (cover.TryGetProperty("extraLarge", out var xl))
                        coverUrl = xl.GetString() ?? string.Empty;
                    else if (cover.TryGetProperty("large", out var lg))
                        coverUrl = lg.GetString() ?? string.Empty;
                }

                results.Add(new SearchResult
                {
                    Title = title,
                    Url = $"{BaseUrl}/anime/{id}",
                    Description = string.Empty,
                    Source = SourceName,
                });
            }

            Logger.LogDebug("AnimeX GraphQL search returned {Count} results", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AnimeX GraphQL search failed");
            return new List<SearchResult>();
        }
    }

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
        foreach (Match g in Regex.Matches(html,
            @"<a[^>]*href=""/anime[^""]*\?genres?=[^""]+""[^>]*>(?<genre>[^<]+)</a>"))
        {
            var genre = DecodeHtml(g.Groups["genre"].Value.Trim());
            if (!string.IsNullOrEmpty(genre))
                genres.Add(genre);
        }

        return new SeriesInfo
        {
            Title = title,
            Url = seriesUrl,
            CoverImageUrl = coverUrl,
            Description = description,
            Genres = genres.Distinct().ToList(),
            Seasons = new List<SeasonRef>(),
            HasMovies = false,
        };
    }

    public override async Task<List<EpisodeRef>> GetEpisodesAsync(string seasonUrl, CancellationToken cancellationToken = default)
    {
        var html = await FetchPageAsync(seasonUrl, cancellationToken).ConfigureAwait(false);

        var episodes = new List<EpisodeRef>();
        var episodeUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AxEpisodeItem.Matches(html))
        {
            var url = match.Groups["url"].Value;
            if (!episodeUrls.Add(url))
                continue;

            var numStr = match.Groups["num"].Value;
            if (!int.TryParse(numStr, out var num))
            {
                if (!int.TryParse(match.Groups["num2"].Value, out num))
                    continue;
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
                providers[langKey] = new Dictionary<string, string>();

            var providerName = server switch
            {
                "voe" => "VOE",
                "vidmoly" => "Vidmoly",
                "filemoon" => "Filemoon",
                "vidoza" => "Vidoza",
                _ => server,
            };

            if (!providers[langKey].ContainsKey(providerName))
                providers[langKey][providerName] = episodeUrl;
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
                    providers[langKey] = new Dictionary<string, string>();

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

    public override async Task<List<BrowseItem>> GetPopularAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("AnimeX fetching popular via GraphQL");

        var payload = new
        {
            query = CatalogQuery,
            variables = new
            {
                filter = new { },
                sort = new[] { new { field = "POPULARITY", direction = "DESC" } },
                limit = 24,
                offset = 0
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await HttpClient.PostAsync($"{GraphQlApi}/graphql", content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseGraphQlBrowseItems(responseJson);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AnimeX GraphQL popular fetch failed");
            return new List<BrowseItem>();
        }
    }

    public override async Task<List<BrowseItem>> GetNewReleasesAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("AnimeX fetching new releases via API");

        try
        {
            var response = await HttpClient.GetAsync($"{GraphQlApi}/api/recent?page=1", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);

            var items = new List<BrowseItem>();
            if (doc.RootElement.TryGetProperty("results", out var results))
            {
                foreach (var item in results.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString() ?? string.Empty;
                    var titleRomaji = item.GetProperty("titleRomaji").GetString() ?? string.Empty;
                    var titleEnglish = item.TryGetProperty("titleEnglish", out var te) ? te.GetString() ?? string.Empty : string.Empty;
                    var title = string.IsNullOrEmpty(titleEnglish) ? titleRomaji : titleEnglish;

                    string coverUrl = string.Empty;
                    if (item.TryGetProperty("coverImage", out var cover) && cover.ValueKind == JsonValueKind.Object)
                    {
                        if (cover.TryGetProperty("extraLarge", out var xl))
                            coverUrl = xl.GetString() ?? string.Empty;
                        else if (cover.TryGetProperty("large", out var lg))
                            coverUrl = lg.GetString() ?? string.Empty;
                    }

                    items.Add(new BrowseItem
                    {
                        Title = title,
                        Url = $"{BaseUrl}/anime/{id}",
                        CoverImageUrl = coverUrl,
                        Genre = string.Empty,
                        Source = SourceName,
                    });
                }
            }

            return items;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AnimeX new releases fetch failed");
            return new List<BrowseItem>();
        }
    }

    private List<BrowseItem> ParseGraphQlBrowseItems(string responseJson)
    {
        var items = new List<BrowseItem>();
        using var doc = JsonDocument.Parse(responseJson);

        var root = doc.RootElement.GetProperty("data").GetProperty("catalogAnime").GetProperty("items");
        foreach (var item in root.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? string.Empty;
            var titleRomaji = item.GetProperty("titleRomaji").GetString() ?? string.Empty;
            var titleEnglish = item.TryGetProperty("titleEnglish", out var te) ? te.GetString() ?? string.Empty : string.Empty;
            var title = string.IsNullOrEmpty(titleEnglish) ? titleRomaji : titleEnglish;

            string coverUrl = string.Empty;
            if (item.TryGetProperty("coverImage", out var cover) && cover.ValueKind == JsonValueKind.Object)
            {
                if (cover.TryGetProperty("extraLarge", out var xl))
                    coverUrl = xl.GetString() ?? string.Empty;
                else if (cover.TryGetProperty("large", out var lg))
                    coverUrl = lg.GetString() ?? string.Empty;
            }

            items.Add(new BrowseItem
            {
                Title = title,
                Url = $"{BaseUrl}/anime/{id}",
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
            return string.Empty;

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            return $"https:{url}";

        return $"{BaseUrl}{url}";
    }
}
