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

> **Make sure you are in the app, not the resource group.** Creating the app also created a
> *resource group* (a folder) called something like `chartertrip_group`, and it is easy to land
> there instead. Check the small grey text under the page title:
>
> - `Resource group` — wrong level. Its sidebar only shows Deployments, Security, Policies, Locks.
>   Click **Overview**, then click the row whose **Type** is **App Service**.
> - `App Service` — correct. The sidebar now has Deployment Center, Environment variables, Scale up,
>   Scale out.
>
> The group usually holds two things: the **App Service** (your app and its settings) and the
> **App Service plan** (the hardware tier, and the thing that actually bills you).

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

> **"Basic authentication is disabled."** Azure turns basic auth off by default on new App
> Services, and a publish profile *is* a basic auth credential, so the download refuses. Turn it on:
>
> **Settings → Configuration → General settings → Platform settings →
> SCM Basic Auth Publishing Credentials → On → Save.**
>
> Then come back to Overview and download it. (The more modern alternative is OIDC federated
> credentials via `azure/login`, which stores no secret at all — more setup, worth doing later.)

1. In the Portal, open your Web App → **Overview** → **Download publish profile** in the top
   toolbar. If you cannot see it, it is under the **⋯** (More) menu there. You get a
   `.publishsettings` file — open it in a text editor and copy **all** of it.
2. In GitHub: repo → **Settings** → **Secrets and variables** → **Actions** → **New repository
   secret**.
   - Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
   - Value: the entire file contents
3. Check two values on the **Overview** page and make `.github/workflows/deploy.yml` match:

   | Portal field | Workflow variable | Note |
   | --- | --- | --- |
   | **Name** (top of the page) | `AZURE_WEBAPP_NAME` | What the deploy targets |
   | **Default domain** | `AZURE_WEBAPP_HOSTNAME` | What the smoke test curls |

   These are **not** the same thing any more. Azure appends a random suffix to new apps, so an app
   named `chartertrip` is served at something like
   `chartertrip-ggeddmesa6d7hbbd.centralus-01.azurewebsites.net`. Copy the default domain verbatim.

> **Where am I?** The left sidebar of a Web App is long and grouped into collapsible sections.
> Everything in these steps lives under **Settings**, except the publish profile, which is a button
> on **Overview**. If the sidebar looks short, widen the browser window or click the **»** to expand it.

That file is a credential. It goes in GitHub Secrets and nowhere else — never committed.

## Step 5 — Push

```bash
git push origin main
```

Watch the **Actions** tab. When it goes green:

- `https://<default-domain>` — the site (the **Default domain** from the Overview page)
- `https://<default-domain>/healthz` — should return
  `{"status":"healthy","revision":0,"people":26,...}`

## Step 6 — Prove the data actually persists

The single most important thing to verify early, because finding out it does not work in nine
days' time is a bad evening:

1. Open the deployed site, edit an itinerary item.
2. Push any trivial commit to `main` and let it redeploy.
3. Reload. **Your edit should still be there.**

If it reset, `Trip__DataRoot` is not pointing at `/home/data`. Fix step 2.

You do not have to wait for a deploy to find out. `/healthz` reports what the store thinks of
itself:

```bash
curl -s https://<your-app>.azurewebsites.net/healthz
```

```json
{ "status": "healthy", "revision": 41, "dataPath": "/home/data/trip.json",
  "seeded": false, "canPersist": true }
```

- **`seeded: true`** on a site that has been in use — the app could not find its file and started
  from the built-in seed. Every edit made since the last restart is the only data it has.
- **`canPersist: false`** — the data directory is read-only. Nothing is being saved at all.
- **`dataPath`** not under `/home` — the directory is disposable and will be wiped on the next
  push. This is the usual cause of the other two.

`status` is `degraded` whenever any of those is true. It still returns 200 on purpose, so a data
problem can never block deploying the fix for it.

---

## Turning on HTTPS-only

**Settings** → **Configuration** → **General settings** → **HTTPS Only: On**. The app already sends
HSTS in production.

## Getting the data out

`trip.json` lives at `/home/data/trip.json`. To read it:

- Portal → your Web App → **Development Tools** → **SSH**, then `cat /home/data/trip.json`
- Or `https://<app>.scm.azurewebsites.net/newui/fileManager` and browse to `data`

Backups are alongside it in `/home/data/backups/`.

## Keeping the seed current

`data/trip.seed.json` is what the app starts from when it finds no data file — a first run, a new
environment, or a data directory that did not survive a deploy. It is a floor, not a backup: the
further it drifts from the real trip, the more a bad day costs.

So refresh it as the weekend gets planned. Download `trip.json` as above, then:

```bash
dotnet run --project tools/CharterTrip.SeedRefresh -- ~/Downloads/trip.json
```

That rewrites `data/trip.seed.json`, keeping everything the host wrote — itinerary, roster, teams,
the Jeopardy board, the mystery cast and their secrets — and dropping everything the weekend
produced: scores, buzzer codes, used clues, which round the mystery is on. A seed that restored a
half-played game would put a scoreboard nobody earned back on the wall.

Nothing is written unless the file parses, so a bad download leaves the existing seed alone. Check
the result before committing:

```bash
dotnet test tests/CharterTrip.Tests
```

`SeedDataTests` asserts the invariants the app relies on — 25 people all on teams, three itinerary
days, a full 5×5 board, a mystery role for everyone. A seed that breaks one of those fails here
rather than on the projector.

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
| Data resets on every deploy | `Trip__DataRoot` is not `/home/data` — check `dataPath` on `/healthz` |
| Itinerary edits revert after a push | Same cause. `/healthz` shows `seeded: true` and a low `revision` |
| Edits randomly disappear | More than one instance is running — go back to step 3 |
| 503 on first hit after a quiet period | F1 free tier cold start. Upgrade to B1 + Always On. |
| Deploy succeeds, site 500s | Check **Log stream** in the Portal; usually a bad app setting |
| "Reconnecting" banner on phones constantly | Expected when a phone sleeps; it recovers on wake |
