# MTG Keyword Guide

A Magic: The Gathering keyword reference and combat simulator, built against the Comprehensive Rules effective September 25, 2026.

| Project | What it is |
|---|---|
| `MagicKeywords.Core` | Shared library: keyword data (`Data/*.json`), the `Keyword` model and the combat simulator engine. No UI. |
| `MagicKeywords.Web` | Blazor WebAssembly PWA (installable, works offline). Published to GitHub Pages. |
| `MagicKeywords` | Windows desktop app (WPF). |

## Run the web app locally

```
dotnet run --project MagicKeywords.Web
```

## Deploy to GitHub Pages

1. Push this folder to a GitHub repository whose default branch is `main`.
2. In the repository go to **Settings → Pages** and set **Source** to **GitHub Actions**.
3. Every push to `main` runs `.github/workflows/deploy-pages.yml`, which publishes the app to `https://<user>.github.io/<repo>/`.

## Install on an iPhone

Open the site in **Safari**, tap **Share → Add to Home Screen**. After the first launch it works offline.
Favorites are stored on the device (use *Favorites → Back up or restore* to keep a copy).

## Updating keyword data

Edit `MagicKeywords.Core/Data/*.json` and rebuild; the desktop and web apps both use the same files.
