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

            // Mkissa
            var mkissa = config.MkissaConfig || {};
            view.querySelector('#chkMkissaEnabled').checked = mkissa.Enabled !== false;
            view.querySelector('#txtMkissaPath1').value = mkissa.DownloadPath1 || mkissa.DownloadPath || '';
            view.querySelector('#txtMkissaPath2').value = mkissa.DownloadPath2 || '';
            view.querySelector('#selMkissaLanguage').value = mkissa.PreferredLanguage || '1';
            view.querySelector('#selMkissaProvider').value = mkissa.PreferredProvider || 'VOE';
            view.querySelector('#selMkissaFallback').value = mkissa.FallbackProvider || '';

            // Miruro
            var miruro = config.MiruroConfig || {};
            view.querySelector('#chkMiruroEnabled').checked = miruro.Enabled !== false;
            view.querySelector('#txtMiruroPath1').value = miruro.DownloadPath1 || miruro.DownloadPath || '';
            view.querySelector('#txtMiruroPath2').value = miruro.DownloadPath2 || '';
            view.querySelector('#selMiruroLanguage').value = miruro.PreferredLanguage || '1';
            view.querySelector('#selMiruroProvider').value = miruro.PreferredProvider || 'VOE';
            view.querySelector('#selMiruroFallback').value = miruro.FallbackProvider || '';

            // Anime.Nexus
            var anime = config.AnimeNexusConfig || {};
            view.querySelector('#chkAnimeNexusEnabled').checked = anime.Enabled !== false;
            view.querySelector('#txtAnimeNexusPath1').value = anime.DownloadPath1 || anime.DownloadPath || '';
            view.querySelector('#txtAnimeNexusPath2').value = anime.DownloadPath2 || '';
            view.querySelector('#selAnimeNexusLanguage').value = anime.PreferredLanguage || '1';
            view.querySelector('#selAnimeNexusProvider').value = anime.PreferredProvider || 'VOE';
            view.querySelector('#selAnimeNexusFallback').value = anime.FallbackProvider || '';

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

            // Mkissa
            if (!config.MkissaConfig) config.MkissaConfig = {};
            config.MkissaConfig.Enabled = view.querySelector('#chkMkissaEnabled').checked;
            config.MkissaConfig.DownloadPath1 = view.querySelector('#txtMkissaPath1').value.trim();
            config.MkissaConfig.DownloadPath2 = view.querySelector('#txtMkissaPath2').value.trim();
            config.MkissaConfig.DownloadPath = config.MkissaConfig.DownloadPath1;
            config.MkissaConfig.PreferredLanguage = view.querySelector('#selMkissaLanguage').value;
            config.MkissaConfig.PreferredProvider = view.querySelector('#selMkissaProvider').value;
            config.MkissaConfig.FallbackProvider = view.querySelector('#selMkissaFallback').value;

            // Miruro
            if (!config.MiruroConfig) config.MiruroConfig = {};
            config.MiruroConfig.Enabled = view.querySelector('#chkMiruroEnabled').checked;
            config.MiruroConfig.DownloadPath1 = view.querySelector('#txtMiruroPath1').value.trim();
            config.MiruroConfig.DownloadPath2 = view.querySelector('#txtMiruroPath2').value.trim();
            config.MiruroConfig.DownloadPath = config.MiruroConfig.DownloadPath1;
            config.MiruroConfig.PreferredLanguage = view.querySelector('#selMiruroLanguage').value;
            config.MiruroConfig.PreferredProvider = view.querySelector('#selMiruroProvider').value;
            config.MiruroConfig.FallbackProvider = view.querySelector('#selMiruroFallback').value;

            // Anime.Nexus
            if (!config.AnimeNexusConfig) config.AnimeNexusConfig = {};
            config.AnimeNexusConfig.Enabled = view.querySelector('#chkAnimeNexusEnabled').checked;
            config.AnimeNexusConfig.DownloadPath1 = view.querySelector('#txtAnimeNexusPath1').value.trim();
            config.AnimeNexusConfig.DownloadPath2 = view.querySelector('#txtAnimeNexusPath2').value.trim();
            config.AnimeNexusConfig.DownloadPath = config.AnimeNexusConfig.DownloadPath1;
            config.AnimeNexusConfig.PreferredLanguage = view.querySelector('#selAnimeNexusLanguage').value;
            config.AnimeNexusConfig.PreferredProvider = view.querySelector('#selAnimeNexusProvider').value;
            config.AnimeNexusConfig.FallbackProvider = view.querySelector('#selAnimeNexusFallback').value;

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
