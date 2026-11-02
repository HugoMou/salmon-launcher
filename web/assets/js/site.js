/* SalmonLauncher website — interaction layer
   Copyright (C) 2026 Hugo Mou — GPL-3.0-or-later */
(function () {
  "use strict";

  var LANG_KEY = "sl-lang";
  var cache = new Map();

  /* ---------- language ---------- */
  function remember(el) {
    if (!cache.has(el)) {
      cache.set(el, { text: el.textContent, html: el.innerHTML });
    }
  }

  function applyLang(lang) {
    document.documentElement.dataset.lang = lang;
    document.documentElement.lang = lang === "zh" ? "zh-CN" : "en";

    document.querySelectorAll("[data-zh]").forEach(function (el) {
      remember(el);
      var base = cache.get(el);
      var zh = el.dataset.zh;
      if (lang === "zh" && zh) {
        el.textContent = zh;
      } else {
        el.textContent = base.text;
      }
    });

    document.querySelectorAll("[data-zh-html]").forEach(function (el) {
      remember(el);
      var base = cache.get(el);
      var zh = el.dataset.zhHtml;
      if (lang === "zh" && zh) {
        el.innerHTML = zh;
      } else {
        el.innerHTML = base.html;
      }
    });

    document.querySelectorAll("title[data-zh-title]").forEach(function (el) {
      remember(el);
      var base = cache.get(el);
      el.textContent =
        lang === "zh" && el.dataset.zhTitle ? el.dataset.zhTitle : base.text;
    });

    document
      .querySelectorAll('meta[name="description"][data-zh-content]')
      .forEach(function (el) {
        remember(el);
        var base = cache.get(el);
        el.setAttribute(
          "content",
          lang === "zh" && el.dataset.zhContent
            ? el.dataset.zhContent
            : base.text
        );
      });

    document.querySelectorAll("[data-set-lang]").forEach(function (btn) {
      btn.setAttribute(
        "aria-pressed",
        String(btn.dataset.setLang === lang)
      );
    });

    try {
      localStorage.setItem(LANG_KEY, lang);
    } catch (e) {
      /* storage may be unavailable */
    }
  }

  function initLang() {
    document.querySelectorAll("[data-zh]").forEach(remember);
    document.querySelectorAll("[data-zh-html]").forEach(remember);

    var saved = null;
    try {
      saved = localStorage.getItem(LANG_KEY);
    } catch (e) {
      saved = null;
    }
    applyLang(saved === "zh" ? "zh" : "en");

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
