/* SalmonLauncher website — release sync from GitHub
   Copyright (C) 2026 Hugo Mou — GPL-3.0-or-later */
(function () {
  "use strict";

  var API =
    "https://api.github.com/repos/HugoMou/salmonlauncher/releases?per_page=100";
  var CACHE_KEY = "sl-releases-v1";
  var TTL = 60 * 60 * 1000;
  var ASSET_PREFIX =
    "https://github.com/HugoMou/salmonlauncher/releases/download/";

  function escapeRegExp(value) {
    return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  }

  /* release notes list each file next to its SHA-256 */
  function hashFor(body, name) {
    if (!body || !name) return "";
    var re = new RegExp(
      "^\\s*" + escapeRegExp(name) + "\\s+([A-Fa-f0-9]{64})\\s*$",
      "m"
    );
    var match = body.match(re);
    return match ? match[1].toUpperCase() : "";
  }

  function normalize(release) {
    return {
      tag: release.tag_name || "",
      date: (release.published_at || "").slice(0, 10),
      pre: !!release.prerelease,
      assets: (release.assets || []).map(function (asset) {
        return {
          name: asset.name || "",
          size: asset.size || 0,
          url: asset.browser_download_url || "",
          sha256: hashFor(release.body, asset.name || "")
        };
      })
    };
  }

  function readCache(ignoreAge) {
    try {
      var raw = localStorage.getItem(CACHE_KEY);
      if (!raw) return null;
      var box = JSON.parse(raw);
      if (!box || !box.data || !box.data.length) return null;
      if (!ignoreAge && Date.now() - (box.at || 0) > TTL) return null;
      return box.data;
    } catch (e) {
      return null;
    }
  }

  function writeCache(data) {
    try {
      localStorage.setItem(CACHE_KEY, JSON.stringify({ at: Date.now(), data: data }));
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

  function safeUrl(url) {
    return typeof url === "string" && url.indexOf(ASSET_PREFIX) === 0;
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

  function render(releases) {
    if (!releases.length) return;

    var latest = null;
    for (var i = 0; i < releases.length; i++) {
      if (!releases[i].pre) {
        latest = releases[i];
        break;
      }
    }
    if (!latest) latest = releases[0];

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

    fetch(API, { cache: "no-store" })
      .then(function (res) {
        if (!res.ok) throw new Error("HTTP " + res.status);
        return res.json();
      })
      .then(function (list) {
        var data = (Array.isArray(list) ? list : [])
          .filter(function (release) {
            return !release.draft;
          })
          .map(normalize);

        if (!data.length) throw new Error("empty release list");
        writeCache(data);
        render(data);
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
