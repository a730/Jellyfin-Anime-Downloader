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

    private const string SearchQuery = @"
        query SearchAnime($q: String!, $limit: Int) {
            catalogAnime(filter: { query: $q, includeAdult: false }, limit: $limit) {
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

    private const string AnimeQuery = @"
        query GetAnime($anilistId: Int!) {
            anime(anilistId: $anilistId) {
                id
                anilistId
                malId
                titleRomaji
                titleEnglish
                coverImage
                bannerImage
                description
                status
                format
                type
                episodeCount
                duration
                seasonYear
                season
                averageScore
                genres
                source
                countryOfOrigin
                startDateYear
                startDateMonth
                startDateDay
                endDateYear
                endDateMonth
                endDateDay
            }
        }";

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

    private static string ExtractSlugFromId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return string.Empty;
        var lastDash = id.LastIndexOf('-');
        if (lastDash > 0 && id.Length - lastDash == 6)
            return id[..lastDash];
        return id;
    }

    private static int ExtractAnilistIdFromUrl(string url)
    {
        var match = Regex.Match(url, @"-(\d+)(?:/|$|-)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
            return id;
        return 0;
    }

    private static string BuildAnimeUrl(string id, int anilistId)
    {
        var slug = ExtractSlugFromId(id);
        return $"{slug}-{anilistId}";
    }

    private async Task<JsonElement?> ExecuteGraphQlQueryAsync(string query, object variables, CancellationToken cancellationToken)
    {
        var payload = new { query, variables };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await HttpClient.PostAsync($"{GraphQlApi}/graphql", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("errors", out _))
            return null;

        return doc.RootElement.GetProperty("data").Clone();
    }

    public override async Task<List<SearchResult>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("AnimeX GraphQL search: {Keyword}", keyword);

        try
        {
            var data = await ExecuteGraphQlQueryAsync(SearchQuery, new { q = keyword, limit = 20 }, cancellationToken).ConfigureAwait(false);
            if (data == null)
                return new List<SearchResult>();

            var items = data.Value.GetProperty("catalogAnime").GetProperty("items");

            var results = new List<SearchResult>();
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? string.Empty;
                var anilistId = item.GetProperty("anilistId").GetInt32();
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

                var urlPath = BuildAnimeUrl(id, anilistId);

                results.Add(new SearchResult
                {
                    Title = title,
                    Url = $"{BaseUrl}/anime/{urlPath}",
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
        var anilistId = ExtractAnilistIdFromUrl(seriesUrl);
        if (anilistId == 0)
        {
            Logger.LogWarning("Could not extract anilistId from URL: {Url}", seriesUrl);
            return new SeriesInfo { Title = "Unknown", Url = seriesUrl };
        }

        Logger.LogDebug("AnimeX fetching series info for anilistId: {Id}", anilistId);

        try
        {
            var data = await ExecuteGraphQlQueryAsync(AnimeQuery, new { anilistId }, cancellationToken).ConfigureAwait(false);
            if (data == null)
                return new SeriesInfo { Title = "Unknown", Url = seriesUrl };

            var anime = data.Value.GetProperty("anime");

            var titleRomaji = anime.GetProperty("titleRomaji").GetString() ?? string.Empty;
            var titleEnglish = anime.TryGetProperty("titleEnglish", out var te) ? te.GetString() ?? string.Empty : string.Empty;
            var title = string.IsNullOrEmpty(titleEnglish) ? titleRomaji : titleEnglish;

            string coverUrl = string.Empty;
            if (anime.TryGetProperty("coverImage", out var cover) && cover.ValueKind == JsonValueKind.Object)
            {
                if (cover.TryGetProperty("extraLarge", out var xl))
                    coverUrl = xl.GetString() ?? string.Empty;
                else if (cover.TryGetProperty("large", out var lg))
                    coverUrl = lg.GetString() ?? string.Empty;
            }

            var description = anime.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty;
            var cleanDescription = Regex.Replace(description, "<[^>]+>", string.Empty).Trim();

            var genres = new List<string>();
            if (anime.TryGetProperty("genres", out var genreProp) && genreProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in genreProp.EnumerateArray())
                {
                    var genreName = g.TryGetProperty("name", out var gn) ? gn.GetString() : g.GetString();
                    if (!string.IsNullOrEmpty(genreName))
                        genres.Add(genreName);
                }
            }

            return new SeriesInfo
            {
                Title = DecodeHtml(title),
                Url = seriesUrl,
                CoverImageUrl = coverUrl,
                Description = DecodeHtml(cleanDescription),
                Genres = genres,
                Seasons = new List<SeasonRef>(),
                HasMovies = false,
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AnimeX GraphQL series info failed for anilistId: {Id}", anilistId);
            return new SeriesInfo { Title = "Unknown", Url = seriesUrl };
        }
    }

    public override async Task<List<EpisodeRef>> GetEpisodesAsync(string seasonUrl, CancellationToken cancellationToken = default)
    {
        var anilistId = ExtractAnilistIdFromUrl(seasonUrl);
        if (anilistId == 0)
        {
            Logger.LogWarning("Could not extract anilistId from season URL: {Url}", seasonUrl);
            return new List<EpisodeRef>();
        }

        Logger.LogDebug("AnimeX fetching episodes for anilistId: {Id}", anilistId);

        try
        {
            var data = await ExecuteGraphQlQueryAsync(AnimeQuery, new { anilistId }, cancellationToken).ConfigureAwait(false);
            if (data == null)
                return new List<EpisodeRef>();

            var anime = data.Value.GetProperty("anime");
            var episodeCount = anime.TryGetProperty("episodeCount", out var ec) ? ec.GetInt32() : 0;

            if (episodeCount <= 0)
                return new List<EpisodeRef>();

            var episodes = new List<EpisodeRef>();
            for (int i = 1; i <= episodeCount; i++)
            {
                episodes.Add(new EpisodeRef
                {
                    Url = $"{BaseUrl}/watch/{anilistId}?ep={i}",
                    Number = i,
                    IsMovie = false,
                });
            }

            return episodes;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AnimeX GraphQL episodes failed for anilistId: {Id}", anilistId);
            return new List<EpisodeRef>();
        }
    }

    public override async Task<EpisodeDetails> GetEpisodeDetailsAsync(string episodeUrl, CancellationToken cancellationToken = default)
    {
        var providers = new Dictionary<string, Dictionary<string, string>>();
        var titleEn = string.Empty;

        try
        {
            var html = await FetchPageAsync(episodeUrl, cancellationToken).ConfigureAwait(false);
            var ldMatch = Regex.Match(html,
                @"<script[^>]*type=""application/ld\+json""[^>]*>(?<json>.*?)</script>",
                RegexOptions.Singleline);
            if (ldMatch.Success)
            {
                try
                {
                    using var ldDoc = JsonDocument.Parse(ldMatch.Groups["json"].Value);
                    if (ldDoc.RootElement.TryGetProperty("name", out var nameProp))
                        titleEn = nameProp.GetString() ?? string.Empty;
                }
                catch (JsonException) { }
            }

            var serverPattern = new Regex(
                @"(?:data-server|data-provider|data-source)""?\s*[:=]\s*""?(?<server>[^""'/>]+)""?\s*[},]",
                RegexOptions.Singleline);

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
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "AnimeX episode details scrape failed for: {Url}", episodeUrl);
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
                    var anilistId = item.GetProperty("anilistId").GetInt32();
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

                    var urlPath = BuildAnimeUrl(id, anilistId);

                    items.Add(new BrowseItem
                    {
                        Title = title,
                        Url = $"{BaseUrl}/anime/{urlPath}",
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
            var anilistId = item.GetProperty("anilistId").GetInt32();
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

            var urlPath = BuildAnimeUrl(id, anilistId);

            items.Add(new BrowseItem
            {
                Title = title,
                Url = $"{BaseUrl}/anime/{urlPath}",
                CoverImageUrl = coverUrl,
                Genre = string.Empty,
                Source = SourceName,
            });
        }

        return items;
    }
}
