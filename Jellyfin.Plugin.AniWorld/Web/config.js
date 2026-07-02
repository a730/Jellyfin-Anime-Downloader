export default function (view, params) {
    var pluginId = 'e93d1d02-df60-4545-ae3c-7bb87dff024c';

    function loadConfig() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            // General
            view.querySelector('#txtMaxDownloads').value = config.MaxConcurrentDownloads || 2;
            view.querySelector('#txtMaxRetries').value = config.MaxRetries != null ? config.MaxRetries : 3;
            view.querySelector('#chkAutoScan').checked = config.AutoScanLibrary !== false;
            view.querySelector('#chkNonAdminAccess').checked = config.EnableNonAdminAccess === true;
            view.querySelector('#chkMaintenanceMode').checked = config.MaintenanceMode === true;
            view.querySelector('#txtMaintenanceMessage').value = config.MaintenanceMessage || '';
            view.querySelector('#txtProxyUrl').value = config.ProxyUrl || '';
            view.querySelector('#txtMoviePath').value = config.MovieDownloadPath || '';
            view.querySelector('#txtLanguageFallbackOrder').value = config.LanguageFallbackOrder || '1';

            // AniWorld
            var aw = config.AniWorldConfig || {};
            var awFallback = aw.DownloadPath || config.DownloadPath || '';
            view.querySelector('#chkAniWorldEnabled').checked = aw.Enabled !== false;
            view.querySelector('#txtAniWorldPath1').value = aw.DownloadPath1 || awFallback;
            view.querySelector('#txtAniWorldPath2').value = aw.DownloadPath2 || '';
            view.querySelector('#txtAniWorldPath3').value = aw.DownloadPath3 || '';
            view.querySelector('#selAniWorldLanguage').value = aw.PreferredLanguage || config.PreferredLanguage || '1';
            view.querySelector('#selAniWorldProvider').value = aw.PreferredProvider || config.PreferredProvider || 'VOE';
            view.querySelector('#selAniWorldFallback').value = aw.FallbackProvider || config.FallbackProvider || '';
            view.querySelector('#chkAniWorldOnlyGerman').checked = aw.OnlyGermanLanguages === true;

            // s.to
            var sto = config.StoConfig || {};
            var stoFallback = sto.DownloadPath || '';
            view.querySelector('#chkStoEnabled').checked = sto.Enabled === true;
            view.querySelector('#txtStoBaseUrl').value = config.StoBaseUrl || '';
            view.querySelector('#txtStoPath1').value = sto.DownloadPath1 || stoFallback;
            view.querySelector('#txtStoPath2').value = sto.DownloadPath2 || '';
            view.querySelector('#selStoLanguage').value = sto.PreferredLanguage || '1';
            view.querySelector('#selStoProvider').value = sto.PreferredProvider || 'VOE';
            view.querySelector('#selStoFallback').value = sto.FallbackProvider || '';

            // AniWatch
            var aniwatch = config.AniWatchConfig || {};
            view.querySelector('#chkAniWatchEnabled').checked = aniwatch.Enabled !== false;
            view.querySelector('#txtAniWatchPath1').value = aniwatch.DownloadPath1 || aniwatch.DownloadPath || '';
            view.querySelector('#txtAniWatchPath2').value = aniwatch.DownloadPath2 || '';
            view.querySelector('#selAniWatchLanguage').value = aniwatch.PreferredLanguage || '1';
            view.querySelector('#selAniWatchProvider').value = aniwatch.PreferredProvider || 'VOE';
            view.querySelector('#selAniWatchFallback').value = aniwatch.FallbackProvider || '';

            // AnimeX
            var animex = config.AnimeXConfig || {};
            view.querySelector('#chkAnimeXEnabled').checked = animex.Enabled !== false;
            view.querySelector('#txtAnimeXPath1').value = animex.DownloadPath1 || animex.DownloadPath || '';
            view.querySelector('#txtAnimeXPath2').value = animex.DownloadPath2 || '';
            view.querySelector('#selAnimeXLanguage').value = animex.PreferredLanguage || '1';
            view.querySelector('#selAnimeXProvider').value = animex.PreferredProvider || 'VOE';
            view.querySelector('#selAnimeXFallback').value = animex.FallbackProvider || '';

            Dashboard.hideLoadingMsg();
        });
    }

    function saveConfig() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            // General
            config.MaxConcurrentDownloads = parseInt(view.querySelector('#txtMaxDownloads').value, 10) || 2;
            config.MaxRetries = parseInt(view.querySelector('#txtMaxRetries').value, 10) || 0;
            config.AutoScanLibrary = view.querySelector('#chkAutoScan').checked;
            config.EnableNonAdminAccess = view.querySelector('#chkNonAdminAccess').checked;
            config.MaintenanceMode = view.querySelector('#chkMaintenanceMode').checked;
            config.MaintenanceMessage = view.querySelector('#txtMaintenanceMessage').value.trim();
            config.ProxyUrl = view.querySelector('#txtProxyUrl').value.trim();
            config.MovieDownloadPath = view.querySelector('#txtMoviePath').value.trim();
            config.LanguageFallbackOrder = view.querySelector('#txtLanguageFallbackOrder').value.trim() || '1';

            // AniWorld
            if (!config.AniWorldConfig) config.AniWorldConfig = {};
            config.AniWorldConfig.Enabled = view.querySelector('#chkAniWorldEnabled').checked;
            config.AniWorldConfig.DownloadPath1 = view.querySelector('#txtAniWorldPath1').value.trim();
            config.AniWorldConfig.DownloadPath2 = view.querySelector('#txtAniWorldPath2').value.trim();
            config.AniWorldConfig.DownloadPath3 = view.querySelector('#txtAniWorldPath3').value.trim();
            config.AniWorldConfig.DownloadPath = config.AniWorldConfig.DownloadPath1;
            config.AniWorldConfig.PreferredLanguage = view.querySelector('#selAniWorldLanguage').value;
            config.AniWorldConfig.PreferredProvider = view.querySelector('#selAniWorldProvider').value;
            config.AniWorldConfig.FallbackProvider = view.querySelector('#selAniWorldFallback').value;
            config.AniWorldConfig.OnlyGermanLanguages = view.querySelector('#chkAniWorldOnlyGerman').checked;

            // Keep legacy flat fields in sync for backward compat
            config.DownloadPath = config.AniWorldConfig.DownloadPath;
            config.PreferredLanguage = config.AniWorldConfig.PreferredLanguage;
            config.PreferredProvider = config.AniWorldConfig.PreferredProvider;
            config.FallbackProvider = config.AniWorldConfig.FallbackProvider;

            // s.to
            if (!config.StoConfig) config.StoConfig = {};
            config.StoConfig.Enabled = view.querySelector('#chkStoEnabled').checked;
            config.StoBaseUrl = view.querySelector('#txtStoBaseUrl').value.trim();
            config.StoConfig.DownloadPath1 = view.querySelector('#txtStoPath1').value.trim();
            config.StoConfig.DownloadPath2 = view.querySelector('#txtStoPath2').value.trim();
            config.StoConfig.DownloadPath = config.StoConfig.DownloadPath1;
            config.StoConfig.PreferredLanguage = view.querySelector('#selStoLanguage').value;
            config.StoConfig.PreferredProvider = view.querySelector('#selStoProvider').value;
            config.StoConfig.FallbackProvider = view.querySelector('#selStoFallback').value;

            // AniWatch
            if (!config.AniWatchConfig) config.AniWatchConfig = {};
            config.AniWatchConfig.Enabled = view.querySelector('#chkAniWatchEnabled').checked;
            config.AniWatchConfig.DownloadPath1 = view.querySelector('#txtAniWatchPath1').value.trim();
            config.AniWatchConfig.DownloadPath2 = view.querySelector('#txtAniWatchPath2').value.trim();
            config.AniWatchConfig.DownloadPath = config.AniWatchConfig.DownloadPath1;
            config.AniWatchConfig.PreferredLanguage = view.querySelector('#selAniWatchLanguage').value;
            config.AniWatchConfig.PreferredProvider = view.querySelector('#selAniWatchProvider').value;
            config.AniWatchConfig.FallbackProvider = view.querySelector('#selAniWatchFallback').value;

            // AnimeX
            if (!config.AnimeXConfig) config.AnimeXConfig = {};
            config.AnimeXConfig.Enabled = view.querySelector('#chkAnimeXEnabled').checked;
            config.AnimeXConfig.DownloadPath1 = view.querySelector('#txtAnimeXPath1').value.trim();
            config.AnimeXConfig.DownloadPath2 = view.querySelector('#txtAnimeXPath2').value.trim();
            config.AnimeXConfig.DownloadPath = config.AnimeXConfig.DownloadPath1;
            config.AnimeXConfig.PreferredLanguage = view.querySelector('#selAnimeXLanguage').value;
            config.AnimeXConfig.PreferredProvider = view.querySelector('#selAnimeXProvider').value;
            config.AnimeXConfig.FallbackProvider = view.querySelector('#selAnimeXFallback').value;

            ApiClient.updatePluginConfiguration(pluginId, config).then(function () {
                Dashboard.processPluginConfigurationUpdateResult();
            });
        });
    }

    view.addEventListener('viewshow', function () {
        loadConfig();
    });

    view.querySelector('#AniWorldConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        saveConfig();
        return false;
    });
}
