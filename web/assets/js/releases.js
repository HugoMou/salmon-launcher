/* SalmonLauncher website — release data mirrored by the site server
   Copyright (C) 2026 Hugo Mou — GPL-3.0-or-later */
(function () {
  "use strict";

  /* Written by scripts/sync-web-releases.sh on the server; all asset URLs are
     local mirrors under downloads/, so visitors never leave the site. */
  var DATA_URL = "assets/data/releases.json";
  var CACHE_KEY = "sl-releases-v2";
  var TTL = 30 * 60 * 1000;
  var ASSET_PREFIX = "downloads/";

  function normalize(release) {
    return {
      tag: (release && release.tag) || "",
      date: (release && release.date) || "",
      pre: !!(release && release.pre),
      assets: ((release && release.assets) || []).map(function (asset) {
        return {
          name: asset.name || "",
          size: asset.size || 0,
          url: asset.url || "",
          sha256: asset.sha256 || ""
        };
      })
    };
  }

  function readCache(ignoreAge) {
    try {
      var raw = localStorage.getItem(CACHE_KEY);
      if (!raw) return null;
      var box = JSON.parse(raw);
      if (!box || !box.payload || !box.payload.releases) return null;
      if (!box.payload.releases.length) return null;
      if (!ignoreAge && Date.now() - (box.at || 0) > TTL) return null;
      return box.payload;
    } catch (e) {
      return null;
    }
  }

  function writeCache(payload) {
    try {
      localStorage.setItem(
        CACHE_KEY,
        JSON.stringify({ at: Date.now(), payload: payload })
      );
    } catch (e) {
      /* storage may be unavailable */
    }
  }

  function formatSize(bytes) {
    if (!bytes) return "";
    var mb = bytes / 1048576;
    var text = mb >= 100 ? mb.toFixed(0) : mb.toFixed(1);
    return text.replace(/\.0$/, "") + " MB";
  }

  /* Only same-origin mirrors written by the sync script are accepted. */
  function safeUrl(url) {
    return (
      typeof url === "string" &&
      url.indexOf(ASSET_PREFIX) === 0 &&
      url.indexOf("..") === -1 &&
      url.indexOf("\\") === -1 &&
      url.indexOf("://") === -1
    );
  }

  function pickAsset(release, kind) {
    var assets = release.assets || [];
    var ext = kind === "installer" ? ".exe" : ".zip";
    var word = kind === "installer" ? "setup" : "portable";
    var i;

    for (i = 0; i < assets.length; i++) {
      var named = assets[i].name.toLowerCase();
      if (named.slice(-ext.length) === ext && named.indexOf(word) !== -1) {
        return assets[i];
      }
    }
    for (i = 0; i < assets.length; i++) {
      if (assets[i].name.toLowerCase().slice(-ext.length) === ext) {
        return assets[i];
      }
    }
    return null;
  }

  function setText(selector, value) {
    if (!value) return;
    document.querySelectorAll(selector).forEach(function (el) {
      el.textContent = value;
    });
  }

  function renderSynced(syncedAt) {
    if (!syncedAt) return;
    document.querySelectorAll("[data-sl-synced]").forEach(function (el) {
      el.textContent = "SYNCED " + syncedAt.slice(0, 10);
      el.removeAttribute("hidden");
    });
  }

  function renderLatest(release) {
    setText("[data-sl-latest-tag]", release.tag);
    setText("[data-sl-latest-date]", release.date);

    ["installer", "portable"].forEach(function (kind) {
      var asset = pickAsset(release, kind);
      if (!asset) return;

      setText('[data-sl-asset-name="' + kind + '"]', asset.name);
      setText('[data-sl-asset-size="' + kind + '"]', formatSize(asset.size));

      document
        .querySelectorAll('[data-sl-asset-hash="' + kind + '"]')
        .forEach(function (el) {
          if (asset.sha256) el.textContent = asset.sha256;
        });

      document
        .querySelectorAll('[data-sl-asset-link="' + kind + '"]')
        .forEach(function (el) {
          if (safeUrl(asset.url)) el.setAttribute("href", asset.url);
        });
    });
  }

  function buildRow(release, isLatest) {
    var row = document.createElement("div");
    row.className = "rel-row" + (isLatest ? " latest" : "");

    var tag = document.createElement("span");
    tag.className = "rel-tag";
    tag.textContent = release.tag;
    row.appendChild(tag);

    if (isLatest || release.pre) {
      var badge = document.createElement("span");
      badge.className = "badge";
      badge.textContent = isLatest ? "LATEST" : "PRE";
      row.appendChild(badge);
    }

    var date = document.createElement("span");
    date.className = "rel-date";
    date.textContent = release.date;
    row.appendChild(date);

    var files = document.createElement("div");
    files.className = "rel-files";

    ["installer", "portable"].forEach(function (kind) {
      var asset = pickAsset(release, kind);
      if (!asset || !safeUrl(asset.url)) return;

      var link = document.createElement("a");
      link.className = "rel-dl";
      link.href = asset.url;
      link.setAttribute("download", "");
      link.setAttribute("aria-label", asset.name);
      link.textContent = kind === "installer" ? "EXE " : "ZIP ";

      var size = document.createElement("em");
      size.textContent = formatSize(asset.size);
      link.appendChild(size);

      files.appendChild(link);
    });

    row.appendChild(files);
    return row;
  }

  function renderHistory(releases) {
    var host = document.querySelector("[data-sl-history]");
    if (!host) return;

    var latestIndex = -1;
    for (var i = 0; i < releases.length; i++) {
      if (!releases[i].pre) {
        latestIndex = i;
        break;
      }
    }

    host.innerHTML = "";
    releases.forEach(function (release, index) {
      host.appendChild(buildRow(release, index === latestIndex));
    });
  }

  function render(payload) {
    var releases = ((payload && payload.releases) || [])
      .map(normalize)
      .filter(function (release) {
        return release.tag && release.assets.length;
      });
    if (!releases.length) return;

    var latest = null;
    for (var i = 0; i < releases.length; i++) {
      if (!releases[i].pre) {
        latest = releases[i];
        break;
      }
    }
    if (!latest) latest = releases[0];

    renderSynced(payload.syncedAt);
    renderLatest(latest);
    renderHistory(releases);
  }

  function boot() {
    if (!document.querySelector("[data-sl-history]")) return;

    var cached = readCache(false);
    if (cached) {
      render(cached);
      return;
    }

    var stale = readCache(true);

    fetch(DATA_URL, { cache: "no-store" })
      .then(function (res) {
        if (!res.ok) throw new Error("HTTP " + res.status);
        return res.json();
      })
      .then(function (payload) {
        if (!payload || !payload.releases || !payload.releases.length) {
          throw new Error("empty release list");
        }
        writeCache(payload);
        render(payload);
      })
      .catch(function () {
        if (stale) render(stale);
      });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
