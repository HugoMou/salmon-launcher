/* SalmonLauncher website — interaction layer
   Copyright (C) 2026 Hugo Mou — GPL-3.0-or-later */
(function () {
  "use strict";

  var LANGS = ["en", "zh", "zh-hant", "ja"];
  var LANG_KEY = "sl-lang";
  var HTML_LANG = { en: "en", zh: "zh-CN", "zh-hant": "zh-Hant", ja: "ja" };
  var DICT = window.SL_I18N || {};
  var nodes = [];

  /* ---------- language ---------- */
  function collect() {
    nodes = [];
    document.querySelectorAll("[data-zh], [data-zh-html]").forEach(function (el) {
      var isHtml = el.hasAttribute("data-zh-html");
      nodes.push({
        el: el,
        kind: isHtml ? "html" : "text",
        en: isHtml ? el.innerHTML : el.textContent,
        zh: isHtml ? el.dataset.zhHtml : el.dataset.zh
      });
    });

    document.querySelectorAll("title[data-zh-title]").forEach(function (el) {
      nodes.push({
        el: el,
        kind: "title",
        en: el.textContent,
        zh: el.dataset.zhTitle
      });
    });

    document
      .querySelectorAll('meta[name="description"][data-zh-content]')
      .forEach(function (el) {
        nodes.push({
          el: el,
          kind: "meta",
          en: el.getAttribute("content") || "",
          zh: el.dataset.zhContent
        });
      });
  }

  function translate(node, lang) {
    if (lang === "en") return null;
    if (lang === "zh") return node.zh || null;

    var table = DICT[lang] || {};
    if (node.zh && table[node.zh] != null) return table[node.zh];

    /* fall back: Traditional Chinese to Simplified, Japanese to English */
    return lang === "zh-hant" ? node.zh || null : null;
  }

  function render(node, value, lang) {
    var next = value == null ? node.en : value;

    if (node.kind === "text") {
      node.el.textContent = next;
    } else if (node.kind === "html") {
      node.el.innerHTML = next;
    } else if (node.kind === "title") {
      node.el.textContent = next;
    } else {
      node.el.setAttribute("content", next);
    }
  }

  function applyLang(lang) {
    if (LANGS.indexOf(lang) === -1) lang = "en";

    document.documentElement.dataset.lang = lang;
    document.documentElement.lang = HTML_LANG[lang];

    nodes.forEach(function (node) {
      render(node, translate(node, lang), lang);
    });

    document.querySelectorAll("[data-set-lang]").forEach(function (btn) {
      btn.setAttribute("aria-pressed", String(btn.dataset.setLang === lang));
    });

    try {
      localStorage.setItem(LANG_KEY, lang);
    } catch (e) {
      /* storage may be unavailable */
    }
  }

  function detectLang() {
    var list = navigator.languages || [navigator.language || "en"];
    for (var i = 0; i < list.length; i++) {
      var tag = String(list[i]).toLowerCase();
      if (tag.indexOf("ja") === 0) return "ja";
      if (
        tag.indexOf("zh-hant") === 0 ||
        tag.indexOf("zh-tw") === 0 ||
        tag.indexOf("zh-hk") === 0 ||
        tag.indexOf("zh-mo") === 0
      ) {
        return "zh-hant";
      }
      if (tag.indexOf("zh") === 0) return "zh";
    }
    return "en";
  }

  function initLang() {
    collect();

    var saved = null;
    try {
      saved = localStorage.getItem(LANG_KEY);
    } catch (e) {
      saved = null;
    }

    applyLang(LANGS.indexOf(saved) !== -1 ? saved : detectLang());

    document.querySelectorAll("[data-set-lang]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        applyLang(btn.dataset.setLang);
      });
    });
  }

  /* ---------- mobile menu ---------- */
  function initMenu() {
    var burger = document.querySelector(".burger");
    var menu = document.querySelector(".mobile-menu");
    if (!burger || !menu) return;
    burger.addEventListener("click", function () {
      var open = menu.classList.toggle("open");
      burger.setAttribute("aria-expanded", String(open));
    });
    menu.querySelectorAll("a").forEach(function (a) {
      a.addEventListener("click", function () {
        menu.classList.remove("open");
        burger.setAttribute("aria-expanded", "false");
      });
    });
  }

  /* ---------- reveal on scroll ---------- */
  function initReveal() {
    var items = document.querySelectorAll(".reveal");
    if (!items.length) return;

    if (!("IntersectionObserver" in window)) {
      items.forEach(function (el) {
        el.classList.add("in");
      });
      return;
    }

    var io = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            entry.target.classList.add("in");
            io.unobserve(entry.target);
          }
        });
      },
      { rootMargin: "0px 0px -12% 0px", threshold: 0.08 }
    );

    items.forEach(function (el) {
      io.observe(el);
    });
  }

  /* ---------- page chrome: progress + nav state ---------- */
  function initChrome() {
    var nav = document.querySelector(".nav");
    var bar = document.createElement("div");
    bar.className = "scroll-progress";
    bar.setAttribute("aria-hidden", "true");
    document.body.appendChild(bar);

    var ticking = false;
    function update() {
      ticking = false;
      var y = window.scrollY || window.pageYOffset;
      var max =
        document.documentElement.scrollHeight - window.innerHeight;
      var p = max > 0 ? y / max : 0;
      bar.style.transform = "scaleX(" + Math.min(1, Math.max(0, p)) + ")";
      bar.classList.toggle("on", y > 8);
      if (nav) nav.classList.toggle("scrolled", y > 8);
    }
    function onScroll() {
      if (ticking) return;
      ticking = true;
      window.requestAnimationFrame(update);
    }
    update();
    window.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("resize", onScroll, { passive: true });
  }

  /* ---------- pointer spotlight ---------- */
  function initSpotlight() {
    var hoverable = window.matchMedia
      ? window.matchMedia("(hover: hover) and (pointer: fine)").matches
      : false;
    if (!hoverable) return;

    var els = document.querySelectorAll(
      ".card, .dl-card, .viz, .stat, .req, .legal, .mock"
    );
    els.forEach(function (el) {
      el.addEventListener(
        "pointermove",
        function (e) {
          var r = el.getBoundingClientRect();
          el.style.setProperty(
            "--mx",
            (((e.clientX - r.left) / r.width) * 100).toFixed(1) + "%"
          );
          el.style.setProperty(
            "--my",
            (((e.clientY - r.top) / r.height) * 100).toFixed(1) + "%"
          );
        },
        { passive: true }
      );
    });
  }

  /* ---------- hero mock tilt ---------- */
  function initTilt() {
    var stage = document.querySelector(".mock-stage");
    var mock = stage && stage.querySelector(".mock");
    if (!mock) return;
    if (!window.matchMedia("(hover: hover) and (pointer: fine)").matches) return;
    if (
      window.matchMedia &&
      window.matchMedia("(prefers-reduced-motion: reduce)").matches
    ) {
      return;
    }

    stage.addEventListener(
      "pointermove",
      function (e) {
        var r = stage.getBoundingClientRect();
        var x = (e.clientX - r.left) / r.width - 0.5;
        var y = (e.clientY - r.top) / r.height - 0.5;
        mock.style.setProperty("--ry", (x * 5).toFixed(2) + "deg");
        mock.style.setProperty("--rx", (-y * 4).toFixed(2) + "deg");
      },
      { passive: true }
    );
    stage.addEventListener("pointerleave", function () {
      mock.style.setProperty("--ry", "0deg");
      mock.style.setProperty("--rx", "0deg");
    });
  }

  /* ---------- copy to clipboard ---------- */
  function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text);
    }
    return new Promise(function (resolve, reject) {
      var ta = document.createElement("textarea");
      ta.value = text;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      try {
        document.execCommand("copy");
        resolve();
      } catch (e) {
        reject(e);
      } finally {
        document.body.removeChild(ta);
      }
    });
  }

  function initCopy() {
    document.querySelectorAll("[data-copy]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var id = btn.dataset.copy;
        var source = id
          ? document.getElementById(id)
          : btn.closest(".code") &&
            btn.closest(".code").querySelector("pre");
        if (!source) return;
        var text = source.textContent.trim();
        copyText(text).then(function () {
          var original = btn.textContent;
          btn.textContent = btn.dataset.copied || "COPIED";
          btn.classList.add("done");
          setTimeout(function () {
            btn.textContent = original;
            btn.classList.remove("done");
          }, 1600);
        });
      });
    });
  }

  /* ---------- small helpers ---------- */
  function initMisc() {
    document.querySelectorAll("[data-year]").forEach(function (el) {
      el.textContent = String(new Date().getFullYear());
    });
  }

  /* ---------- docs scrollspy ---------- */
  function initDocNav() {
    var links = document.querySelectorAll(".doc-nav a[href^='#']");
    if (!links.length || !("IntersectionObserver" in window)) return;
    var map = {};
    links.forEach(function (a) {
      map[a.getAttribute("href").slice(1)] = a;
    });
    var sections = document.querySelectorAll(".doc-body section[id]");
    var io = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (!entry.isIntersecting) return;
          links.forEach(function (a) {
            a.classList.toggle("on", a.getAttribute("href") === "#" + entry.target.id);
          });
        });
      },
      { rootMargin: "-20% 0px -70% 0px" }
    );
    sections.forEach(function (s) {
      io.observe(s);
    });
  }

  function boot() {
    initLang();
    initMenu();
    initReveal();
    initChrome();
    initSpotlight();
    initTilt();
    initCopy();
    initMisc();
    initDocNav();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
