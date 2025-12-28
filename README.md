# MusicSorter

Résumé (français)
-----------------
MusicSorter est une petite application WPF (.NET 8) qui scanne un dossier source contenant des fichiers audio et prépare leur classement dans un dossier cible en se basant sur les tags (AlbumArtist / Performers, Album, Title, Track, Disc, Year). Elle peut soit copier soit déplacer les fichiers vers la nouvelle arborescence. Les opérations sont planifiées après un scan — l'utilisateur peut ensuite appliquer ou annuler.

Fonctionnalités principales
---------------------------
- Scan récursif du dossier source pour détecter fichiers audio (extensions reconnues : .mp3, .flac, .ogg, .m4a, .aac, .wav, .wma).
- Calcul d'un chemin cible par fichier en fonction des tags :
  - Par défaut : `TargetRoot\<Artist>\<Album>\<DD>-<Track> - <Title>.<ext>`
  - Si l'album est manquant : `TargetRoot\<Artist>\<DD>-<Track> - <Title>.<ext>`
  - Si l'artiste est 'Various' (VA/compilation) : `TargetRoot\<Album>\<DD>-<Track> - <Title>.<ext>`
- Gestion des fichiers sidecar (images, .cue, .log, .m3u, .txt, etc.) : associés au dossier album calculé.
- Possibilité de déplacer (Move) ou copier (Copy) les fichiers.
- Détection et gestion d'erreurs :
  - Lignes marquées "PROBLÈME" si un fichier audio du dossier est bloquant.
  - Écriture de fichiers `.pb.txt` contenant le rapport d'erreur pour les fichiers/dossiers problématiques.
- Résumé visible après opérations : total / ok / dossiers problème / done / failed.

Règles de nommage et nettoyage (sanitize)
-----------------------------------------
Lors de la génération des noms de fichiers et dossiers, l'application supprime automatiquement (sans remplacement) les caractères et séquences suivants :
- Caractères interdits explicitement : `< > : " / \ | ? *` et la puce `•`
- Tabulation, saut de ligne, retour chariot (`\t`, `\n`, `\r`)
- Séquences invisibles problématiques communes : U+200B (ZERO WIDTH SPACE), U+FEFF (BOM), U+200E/U+200F, U+2060, et NBSP (`\u00A0`)
- Les espaces et points finals sont retirés (trim de fin de nom)
- Si d'autres caractères invalides subsistent (selon `Path.GetInvalidFileNameChars()`), l'application renvoie une raison `INVALID_CHARS` et remplace ces caractères restants par `_` pour produire une valeur utilisable.
- Si le nom devient vide après nettoyage, la valeur `Unknown` est utilisée et le code d'erreur correspondant est renvoyé (`EMPTY_AFTER_SANITIZE` ou `EMPTY_VALUE`).

Comportement pour dossiers problèmes
------------------------------------
- Si au moins un fichier audio d'un dossier source pose un problème (tag manquant ou erreur de lecture), tout le dossier source est marqué comme `PlannedProblemFolder`.
- Le dossier problème est copié/déplacé sous `TargetRoot\_PROBLEMES\<sanitized source folder name>\...`.
- Pour chaque fichier audio problématique, un fichier `<filename>.pb.txt` contenant le rapport (tags observés, exception, etc.) est écrit à côté du fichier dans le dossier déplacé/copied.

Codes d'erreur et diagnostics
-----------------------------
- Exemples : `MISSING_TAG:ARTIST`, `MISSING_TAG:TITLE`, `MISSING_TAG:TRACK_NUMBER`, `TARGET_PATH_TOO_LONG:<len>`, `ARTIST:INVALID_CHARS`, `ALBUM:RESERVED_NAME`, `UNEXPECTED_EXCEPTION:<Type>`
- Les rapports complets sont écrits dans les `.pb.txt` pour faciliter le débogage.

Dépendances & exigences
-----------------------
- .NET 8
- TagLib# (utilisé pour lire les tags audio)
- Solution/Projet prévu pour être ouvert dans Visual Studio 2022 ou construit avec `dotnet build`.

Utilisation rapide
------------------
1. Lancer l'application.
2. Choisir un dossier Source et un dossier Target existants.
3. Cliquer sur "Scan" — l'application liste les opérations planifiées.
4. Vérifier les lignes (OK / PROBLÈME).
5. Cliquer sur "Apply" pour effectuer les opérations (copie ou déplacement selon l'option).
6. En cas d'erreur d'I/O, des fichiers `.pb.txt` sont créés à côté des fichiers sources et/ou dans les dossiers problèmes.

Notes importantes
-----------------
- L'application vérifie la longueur du chemin final et signale `TARGET_PATH_TOO_LONG` si elle dépasse la limite souple (paramètre `TargetPathSoftMax`).
- Les noms réservés Windows (`CON`, `PRN`, `AUX`, `NUL`, `COM1`..`COM9`, `LPT1`..`LPT9`) sont détectés et modifiés (préfixés/suffixés) pour éviter les collisions.
- Le comportement exact pour les artistes "Various" est maintenu : l'album devient le dossier principal pour ces cas.
- Les opérations sont exécutées avec des protections : journalisation des erreurs et création de rapports `.pb.txt`. L'annulation est possible via le bouton Stop (CancellationToken).

Contribuer
---------
- Forker/Cloner le dépôt.
- Ouvrir la solution dans __Visual Studio 2022__ ou utiliser `dotnet build`.
- Tests manuels recommandés sur un petit jeu de fichiers avant traitement massif.

Licence
-------
- (À renseigner selon ton projet — par défaut indiquer la licence de ton choix.)

Contact
-------
- Voir l'historique Git / repository pour informations de contact et issues.
