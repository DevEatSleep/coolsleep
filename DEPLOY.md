# Guide de déploiement CoolSleep

## Architecture cible

```
GitHub (source)
    │
    ├── push main
    │       │
    │       ├── GitHub Actions → Render
    │       │       └── Container unique (Nginx + ASP.NET Core + FastAPI)
    │       │               URL : https://coolsleep.onrender.com
    │       │
    │       └── GitHub Actions → GitHub Pages (optionnel)
    │               └── Blazor WASM statique
    │                       URL : https://DevEatSleep.github.io/coolsleep
    │
    ├── Open-Meteo (API météo gratuite, aucune clé requise)
    │
    └── UptimeRobot → ping /health toutes les 10 min
                       (évite le sleep du free tier Render)
```

---

## Structure de fichiers attendue

```
coolsleep/
├── Dockerfile
├── render.yaml
├── deploy/
│   ├── nginx.conf
│   └── supervisord.conf
├── CoolSleep.Web/          # Blazor WASM
├── CoolSleep.Api/          # ASP.NET Core
├── CoolSleep.Shared/       # DTOs partagés
├── thermal_service/        # FastAPI Python
│   ├── main.py             # point d'entrée uvicorn (nom requis par supervisord.conf)
│   ├── model.py
│   ├── schemas.py
│   └── requirements.txt
└── .github/
    └── workflows/
        └── deploy.yml
```

---

## Étape 1 — Pousser sur GitHub

```bash
git init
git remote add origin https://github.com/DevEatSleep/coolsleep.git
git add .
git commit -m "chore: initial commit"
git push -u origin main
```

Activer GitHub Pages :
- Settings → Pages → Source : **GitHub Actions**

---

## Étape 2 — Créer le service Render

1. Se connecter sur [render.com](https://render.com) — **pas de CB requise**
2. **New** → **Web Service**
3. Connecter le repo GitHub `coolsleep`
4. Paramètres :
   - Environment : **Docker**
   - Branch : `main`
   - Region : **Frankfurt (EU)**
   - Instance type : **Free**
5. Health check path : `/health`
6. **Create Web Service**

Le premier build prend 5–8 min (images .NET + Python).  
URL générée : `https://coolsleep.onrender.com` (ou similaire).

Alternatively, Render détecte automatiquement `render.yaml` à la racine du repo
et pré-remplit tous les paramètres.

---

## Étape 3 — Configurer le deploy hook

Dans Render → Service → **Settings** → **Deploy hook** :
- Copier l'URL du hook

Dans GitHub → Settings → **Secrets and variables** → Actions :
- Nouveau secret : `RENDER_DEPLOY_HOOK` = URL copiée

Chaque `git push main` déclenchera désormais un redéploiement automatique.

---

## Étape 4 — Configurer UptimeRobot (anti-sleep)

Le free tier Render met le service en veille après 15 min sans trafic.
Cold start : 30–60s — acceptable mais gênant en canicule.

Solution : [UptimeRobot](https://uptimerobot.com) gratuit, sans CB.

1. Créer un compte UptimeRobot
2. **Add New Monitor** :
   - Type : **HTTP(s)**
   - URL : `https://coolsleep.onrender.com/health`
   - Interval : **5 minutes**
3. Save

Le service reste éveillé en permanence. En dehors des périodes de canicule,
tu peux désactiver le monitor pour laisser Render économiser des ressources.

---

## Étape 5 — Configurer la base href Blazor

Si tu utilises **GitHub Pages** pour le front, adapter dans `deploy.yml` :
```yaml
sed -i 's|<base href="/" />|<base href="/coolsleep/" />|g' ...
```
Remplacer `/coolsleep/` par le nom exact de ton repo GitHub.

Si tu sers le front **depuis Render/Nginx** (recommandé — un seul domaine,
pas de problème CORS), laisser `<base href="/" />` — rien à changer.

---

## Étape 6 — Variables d'environnement

Aucune clé API requise pour le MVP (Open-Meteo est gratuit et sans auth).

Si tu ajoutes Mistral plus tard :
- Render → Service → **Environment** → Add environment variable
- `MISTRAL_API_KEY` = ta clé
- Ne jamais committer de clé dans le repo

---

## Vérification post-déploiement

```bash
BASE=https://coolsleep.onrender.com

# Health check global
curl $BASE/health

# API .NET
curl $BASE/api/health

# FastAPI thermique
curl $BASE/thermal/health

# Plan de nuit — test complet
curl -s -X POST $BASE/thermal/compute \
  -H "Content-Type: application/json" \
  -d '{
    "hourly_temps": [30,29,28,27,26,25,24,23,23,23,24,25,26,28,29],
    "hourly_humidity": [45,45,45,45,45,45,45,45,45,45,45,45,45,45,45],
    "daytime_temps": [28,30,32,34,36,37,38,37],
    "housing": "appart_haut",
    "indoor_temp_start": 26.0,
    "volets_fermes": true
  }' | python3 -m json.tool
```

---

## Limites du free tier Render

| Limite | Valeur |
|---|---|
| CB requise | **Non** ✓ |
| RAM | 512 MB |
| CPU | 0.1 vCPU partagé |
| Bande passante | 100 GB/mois |
| Sleep sans trafic | 15 min (contourné par UptimeRobot) |
| Build minutes | 500/mois |
| Région | Frankfurt (EU) ✓ |

Pour CoolSleep (calcul léger, trafic sporadique) : largement suffisant.

---

## Mise à jour

```bash
git add .
git commit -m "fix: ..."
git push
# → GitHub Actions déclenche automatiquement Render + GitHub Pages
```

Durée d'un redéploiement : ~3–5 min.

---

## Rollback rapide

```bash
# Option 1 — Git
git revert HEAD --no-edit
git push

# Option 2 — Render dashboard
# Render → Service → Deploys → choisir un déploiement antérieur → Redeploy
```
