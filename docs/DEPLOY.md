# Deploying to Azure App Service

Written for someone who has never used Azure. Roughly 15 minutes end to end.

## What you are building

**App Service** is a managed box that runs your app. You never see the OS, never patch it, never
install .NET on it. You hand it a published build; it runs it and gives you an HTTPS URL with a
certificate. Think "IIS as a service".

**GitHub Actions** is a robot that watches the repo. On every push it builds and tests. On pushes
to `main` it also publishes and hands the output to Azure.

---

## Step 1 — Create the App Service

1. Go to the [Azure Portal](https://portal.azure.com) → **Create a resource** → **Web App**.
2. Fill in:
   | Field | Value |
   | --- | --- |
   | Name | `chartertrip` (this becomes `chartertrip.azurewebsites.net`) |
   | Publish | **Code** |
   | Runtime stack | **.NET 10 (LTS)** |
   | Operating System | **Linux** |
   | Region | whichever is closest to Texas |
   | Pricing plan | **F1 Free** to start |
3. **Review + create**.

> **If the portal does not offer .NET 10 yet**, pick .NET 8 and see *Fallback* at the bottom —
> the app can carry its own runtime instead.

**F1 free** sleeps when idle, so the first request after a quiet spell takes a few seconds. That is
fine for planning. Before the trip, switch the plan to **B1** (about $13/month) and turn on
**Always On** so it is instant all weekend, then scale back down afterwards.

## Step 2 — Tell it where to keep the data

From the "deployment is complete" screen, click **Go to resource**. Everything below happens
inside the Web App, using the left-hand sidebar.

**Left sidebar → Settings → Environment variables → App settings tab → + Add**

| Name | Value |
| --- | --- |
| `Trip__DataRoot` | `/home/data` |

Then **Apply** at the bottom, and **Apply** again to confirm. The app restarts.

That double underscore is how ASP.NET spells `Trip:DataRoot` in an environment variable.

**Why `/home`:** almost the entire filesystem on App Service is disposable and gets wiped on
redeploy. `/home` is a network drive that survives. `trip.json`, the backups and the photos all
live there. Put it anywhere else and your data disappears the next time you push.

Click **Apply**.

## Step 3 — Lock it to one instance

**Left sidebar → Settings → Scale out (App Service plan)** → **manual scale**, **1 instance**,
autoscale **off**.

On **F1 Free** this is already fixed at one instance and the controls may be greyed out — nothing
to do today. It matters the moment you move up to B1 for the trip, which is why it is worth knowing
where this page is.

> **This is not optional.** The app keeps the whole trip in memory and owns `trip.json`. Two
> instances means two writers with different pictures of reality, and edits vanish. One instance,
> always.

## Step 4 — Connect GitHub

1. In the Portal, open your Web App → **Overview** → **Download publish profile** in the top
   toolbar. If you cannot see it, it is under the **⋯** (More) menu there. You get a
   `.publishsettings` file — open it in a text editor and copy **all** of it.
2. In GitHub: repo → **Settings** → **Secrets and variables** → **Actions** → **New repository
   secret**.
   - Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
   - Value: the entire file contents
3. Check the app's **Name** on the Overview page. If it is not `chartertrip`, update
   `AZURE_WEBAPP_NAME` near the top of `.github/workflows/deploy.yml` to match exactly — the deploy
   targets that name.

> **Where am I?** The left sidebar of a Web App is long and grouped into collapsible sections.
> Everything in these steps lives under **Settings**, except the publish profile, which is a button
> on **Overview**. If the sidebar looks short, widen the browser window or click the **»** to expand it.

That file is a credential. It goes in GitHub Secrets and nowhere else — never committed.

## Step 5 — Push

```bash
git push origin main
```

Watch the **Actions** tab. When it goes green:

- `https://<your-app>.azurewebsites.net` — the site
- `https://<your-app>.azurewebsites.net/healthz` — should return
  `{"status":"healthy","revision":0,"people":26,...}`

## Step 6 — Prove the data actually persists

The single most important thing to verify early, because finding out it does not work in nine
days' time is a bad evening:

1. Open the deployed site, edit an itinerary item.
2. Push any trivial commit to `main` and let it redeploy.
3. Reload. **Your edit should still be there.**

If it reset, `Trip__DataRoot` is not pointing at `/home/data`. Fix step 2.

---

## Turning on HTTPS-only

**Settings** → **Configuration** → **General settings** → **HTTPS Only: On**. The app already sends
HSTS in production.

## Getting the data out

`trip.json` lives at `/home/data/trip.json`. To read it:

- Portal → your Web App → **Development Tools** → **SSH**, then `cat /home/data/trip.json`
- Or `https://<app>.scm.azurewebsites.net/newui/fileManager` and browse to `data`

Backups are alongside it in `/home/data/backups/`.

## Costs

| Plan | Cost | Notes |
| --- | --- | --- |
| F1 Free | $0 | Sleeps when idle, 60 CPU-minutes/day. Fine for planning. |
| B1 Basic | ~$13/mo | Always On. Worth it for the trip weekend; scale down after. |

You can switch between them at any time under **Scale up (App Service plan)**.

---

## Fallback: if .NET 10 is not offered

Publish the runtime with the app instead of relying on the host having it. In
`.github/workflows/deploy.yml`, change the publish step to:

```yaml
- name: Publish
  run: dotnet publish src/CharterTrip.Web -c Release -r linux-x64 --self-contained true -o ./publish
```

Then in the Portal set **Configuration → General settings → Startup Command** to
`./CharterTrip.Web`. Everything else is unchanged.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| Data resets on every deploy | `Trip__DataRoot` is not `/home/data` |
| Edits randomly disappear | More than one instance is running — go back to step 3 |
| 503 on first hit after a quiet period | F1 free tier cold start. Upgrade to B1 + Always On. |
| Deploy succeeds, site 500s | Check **Log stream** in the Portal; usually a bad app setting |
| "Reconnecting" banner on phones constantly | Expected when a phone sleeps; it recovers on wake |
