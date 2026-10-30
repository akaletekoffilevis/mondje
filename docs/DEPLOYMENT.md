# Déploiement sur MonsterASP.NET

L'application se déploie en **un seul site** : l'interface MVC et l'API REST partagent
la même origine. Un seul dépôt et une seule branche (`main`) suffisent.

## Prérequis

- Compte MonsterASP.NET avec un site ASP.NET Core (.NET 10).
- Base SQL Server créée dans le panneau (une base est incluse dans le plan gratuit).
- Dépôt GitHub : `https://github.com/akaletekoffilevis/mondje.git`.

> Le plan gratuit ne permet qu'**un seul site**. C'est pour cela que l'API est fusionnée
> dans `Mondje.Web` au lieu d'être déployée séparément.

## 1. Configurer Git deploy

Dans le panneau MonsterASP, ouvrir le site puis **Git deploy** :

| Champ | Valeur |
|---|---|
| Repository URL | `https://github.com/akaletekoffilevis/mondje.git` |
| Branch | `main` |
| Deployment type | `Build .NET app` |
| Runtime | `.NET 10` |
| Project | `src/Mondje.Web/Mondje.Web.csproj` |

Le serveur exécute un `dotnet publish` sur la branche `main`, puis démarre
`Mondje.Web.dll`. Aucune branche `api` ou `web` n'est nécessaire.

## 2. Protéger la configuration et les fichiers importés

Dans **Protected files** du site, ajouter :

```text
appsettings.Production.json
uploads/
```

Ainsi, un nouveau déploiement ne remplace ni la chaîne de connexion ni les fichiers
d'import en cours de traitement.

## 3. Créer `appsettings.Production.json` sur le serveur

Copier le modèle `src/Mondje.Web/appsettings.Production.example.json` puis renseigner
la chaîne de connexion fournie par MonsterASP :

```json
{
  "ConnectionStrings": {
    "MondjeDb": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;Encrypt=True"
  },
  "Storage": {
    "ImportFolder": "uploads"
  },
  "Import": {
    "MaxParallel": 3
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

Ce fichier n'est **jamais commité** (il est dans `.gitignore`). Le créer une fois via
FTP/SFTP ou le gestionnaire de fichiers, puis le marquer comme protégé.

Créer également le dossier `uploads/` à la racine du site s'il n'existe pas.

## 4. Déployer

Lancer le déploiement depuis le panneau (ou pousser sur `main` si le déploiement
automatique est activé).

Au démarrage, l'application applique automatiquement les migrations EF Core. Si la base
est momentanément indisponible, le site démarre quand même et l'erreur apparaît dans les
logs ; les pages qui dépendent de la base restent indisponibles jusqu'à la prochaine
tentative.

## 5. Vérifier

Remplacer `https://votre-site` par l'URL fournie par MonsterASP :

```bash
curl -s https://votre-site/api/Status
# {"status":"ok","utc":"...","version":"..."}

curl -s -o /dev/null -w "%{http_code}\n" https://votre-site/
# 200
```

Ouvrir ensuite `https://votre-site/` : le tableau de bord doit afficher le statut API et
les chiffres du grand livre.

## Dépannage

| Symptôme | Cause probable | Solution |
|---|---|---|
| `Invalid object name 'ImportBatches'` | migrations non appliquées | vérifier la chaîne de connexion et redémarrer le site ; les migrations sont lancées au démarrage |
| page d'accueil affiche « API indisponible » | base inaccessible ou `appsettings.Production.json` absent | consulter les logs du site, vérifier la chaîne de connexion |
| `500` sur `/api/...` uniquement | exception applicative | les détails sont dans les logs serveur, le client reçoit un `problem+json` générique |
| imports bloqués | dossier `uploads/` non accessible en écriture | créer le dossier et vérifier les permissions |
| 404 sur `/` | mauvais projet déployé | sélectionner `src/Mondje.Web/Mondje.Web.csproj`, pas un autre projet |

## Mise à jour

Pousser sur `main`, puis redéployer. Les migrations sont additives et les données sont
conservées. `appsettings.Production.json` et `uploads/` restent intacts grâce aux
Protected files.
