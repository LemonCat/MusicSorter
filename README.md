# MusicSorter

Résumé (français)
-----------------
MusicSorter est une petite application WPF (.NET 8) qui scanne un dossier source contenant des fichiers audio et prépare leur classement dans un dossier cible en se basant sur les tags (AlbumArtist / Performers, Album, Title, Track, Disc, Year). Elle peut soit copier soit déplacer les fichiers vers la nouvelle arborescence. Les opérations sont planifiées après un scan — l'utilisateur peut ensuite appliquer ou annuler.

Fonctionnalités principales
---------------------------
- Scan récursif du dossier source pour détecter fichiers audio (extensions reconnues : `.mp3`, `.flac`, `.ogg`, `.m4a`, `.aac`, `.wav`, `.wma`).
- Calcul d'un chemin cible par fichier en fonction des tags :
  - Par défaut : `TargetRoot\<Artist>\<Album>\<DD>-<Track> - <Title>.<ext>`
  - Si l'album est manquant : `TargetRoot\<Artist>\<DD>-<Track> - <Title>.<ext>`
  - Si l'artiste est 'Various' (VA/compilation) : `TargetRoot\<Album>\<DD>-<Track> - <Title>.<ext>`
- Gestion des fichiers sidecar (images, `.cue`, `.log`, `.m3u`, `.txt`, etc.) : associés au dossier album calculé.
- Possibilité de déplacer (`Move`) ou copier (`Copy`) les fichiers.
- Détection et gestion d'erreurs :
  - Lignes marquées `PROBLÈME` si un fichier audio du dossier est bloquant.
  - Écriture de fichiers `.pb.txt` contenant le rapport d'erreur pour les fichiers/dossiers problématiques.
- Résumé visible après opérations : total / ok / dossiers problème / done / failed.

Règles de nommage et nettoyage (sanitize)
-----------------------------------------
Lors de la génération des noms de fichiers et dossiers, l'application applique la politique suivante :

1. Suppression silencieuse (sans erreur) des caractères listés ci‑dessous — ces caractères sont simplement retirés des noms :
   - `<` `>` `:` `"` `/` `\` `|` `?` `*`

2. Suppression silencieuse des caractères et séquences problématiques invisibles :
   - Tabulation, saut de ligne et retour chariot (`\t`, `\n`, `\r`)
   - Séquences invisibles communes : U+200B (ZERO WIDTH SPACE), U+FEFF (BOM), U+200E/U+200F, U+2060, NBSP (`\u00A0`)

3. Trim des espaces et points en fin de nom (Windows n’accepte pas nom se terminant par un espace/point).

4. Si, après suppression des éléments ci‑dessus, il reste d'autres caractères invalides selon `Path.GetInvalidFileNameChars()` :
   - L’application signale la raison `INVALID_CHARS`.
   - Pour produire un nom utilisable, ces caractères restants sont remplacés par `_` (underscore) — la raison est conservée dans la ligne de log (`PbReason`) et dans les rapports `.pb.txt`.

5. Si le nom devient vide après nettoyage, la valeur `Unknown` est utilisée et le code d'erreur correspondant est renvoyé (`EMPTY_AFTER_SANITIZE` ou `EMPTY_VALUE`).

6. Les noms réservés Windows (`CON`, `PRN`, `AUX`, `NUL`, `COM1`..`COM9`, `LPT1`..`LPT9`) sont détectés et modifiés (préfixés/suffixés) pour éviter les collisions.

Remarques et sécurité
--------------------
- Le comportement ci‑dessous garantit que les caractères que vous avez listés sont seulement retirés (pas transformés en underscore) pour produire des chemins plus lisibles tout en restant valides sous Windows.
- Tout autre caractère réellement interdit reste traité comme une erreur (raison `INVALID_CHARS`) et est neutralisé par remplacement (`_`) pour permettre l'écriture du fichier/dossier.
- Tester sur un petit jeu de fichiers avant traitement massif est fortement recommandé (voir la section *Utilisation rapide*).
- En cas d'erreur d'I/O lors d'`Apply`, des fichiers `.pb.txt` sont créés à côté des sources ou dans le dossier `_PROBLEMES` pour diagnostiquer.

Utilisation rapide
------------------
1. Lancer l'application.
2. Choisir un dossier `Source` et un dossier `Target` existants.
3. Cliquer sur `Scan` — l'application liste les opérations planifiées.
4. Vérifier les lignes (`OK` / `PROBLÈME`).
5. Cliquer sur `Apply` pour effectuer les opérations (copie ou déplacement).
6. En cas d'erreur d'I/O, consulter les `.pb.txt` générés.

Dépendances & exigences
-----------------------
- .NET 8
- TagLib# (utilisé pour lire les tags audio)

Contribuer
---------
- Forker/Cloner le dépôt.
- Ouvrir la solution dans __Visual Studio 2022__ ou utiliser `dotnet build`.
- Tests manuels recommandés sur un petit jeu de fichiers avant traitement massif.
