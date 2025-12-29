using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace MusicSorter.ViewModels
{
    public enum RowStatus
    {
        Pending,
        OkPlanned,
        Done,
        FailedApply,
        PlannedProblemFolder
    }

    public enum FileAction
    {
        None,
        Move,
        Copy
    }

    public sealed class LogRow : INotifyPropertyChanged
    {
        private RowStatus _status;
        private FileAction _action;
        private string? _targetPath;
        private string _message = "";
        private DateTime _timestamp = DateTime.Now;
        private string? _pbReason;
        private string? _fileName; // ajouté

        public string SourcePath { get; init; } = "";
        public string? TargetPath { get => _targetPath; set { _targetPath = value; OnPropertyChanged(); } }

        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Title { get; set; }
        public uint Track { get; set; }
        public uint Disc { get; set; }
        public uint Year { get; set; }

        public RowStatus Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public FileAction Action { get => _action; set { _action = value; OnPropertyChanged(); } }

        public string Message { get => _message; set { _message = value; OnPropertyChanged(); } }
        public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); } }

        /// <summary>
        /// Colonne PB (code court + précis).
        /// </summary>
        public string? PbReason { get => _pbReason; set { _pbReason = value; OnPropertyChanged(); } }

        /// <summary>
        /// NOM DU FICHIER SOURCE demandé : FileName
        /// </summary>
        public string? FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } } // ajouté

        /// <summary>
        /// Pour une ligne "dossier PROBLÈME" : lignes "filename\t&lt;contenu pb.txt&gt;".
        /// </summary>
        public string? ProblemItems { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
            
        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            RaiseCanExecuteChanged();
            try { await _execute(); }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter = null) => _execute();

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private string _sourceFolder = "";
        private string _targetFolder = "";
        private bool _moveFiles = false;
        private string _summary = "Prêt.";
        private CancellationTokenSource? _cts;
        private bool _isBusy;

        // Nouveau : contrôle de l'autorisation de "déplacer"
        private bool _moveAllowed = true;
        private readonly HashSet<LogRow> _trackedRows = new();

        public ObservableCollection<LogRow> Rows { get; } = new();

        public string SourceFolder
        {
            get => _sourceFolder;
            set { _sourceFolder = value; OnPropertyChanged(); UpdateCommandStates(); }
        }

        public string TargetFolder
        {
            get => _targetFolder;
            set { _targetFolder = value; OnPropertyChanged(); UpdateCommandStates(); }
        }

        /// <summary>False = Copy, True = Move</summary>
        public bool MoveFiles
        {
            get => _moveFiles;
            set
            {
                // Empêche l'activation si le déplacement n'est pas autorisé
                if (value && !MoveAllowed) return;
                _moveFiles = value;
                OnPropertyChanged();
                UpdateCommandStates();
            }
        }

        public bool MoveAllowed
        {
            get => _moveAllowed;
            private set
            {
                if (_moveAllowed == value) return;
                _moveAllowed = value;
                OnPropertyChanged();
            }
        }

        public string Summary
        {
            get => _summary;
            set { _summary = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(); UpdateCommandStates(); }
        }

        public ICommand ScanCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand StopCommand { get; }

        private readonly AsyncRelayCommand _scanCmd;
        private readonly AsyncRelayCommand _applyCmd;
        private readonly RelayCommand _stopCmd;

        public MainViewModel()
        {
            _scanCmd = new AsyncRelayCommand(ScanAsync, CanScan);
            _applyCmd = new AsyncRelayCommand(ApplyAsync, CanApply);
            _stopCmd = new RelayCommand(Stop, () => IsBusy);

            ScanCommand = _scanCmd;
            ApplyCommand = _applyCmd;
            StopCommand = _stopCmd;

            // Suivre les changements de collection / lignes pour recalculer l'autorisation de "déplacer"
            Rows.CollectionChanged += Rows_CollectionChanged;
            ReevaluateMoveAllowed();
        }

        private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (LogRow? old in e.OldItems.OfType<LogRow>()) DetachRow(old);
            }

            if (e.NewItems != null)
            {
                foreach (LogRow? nw in e.NewItems.OfType<LogRow>()) AttachRow(nw);
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var r in _trackedRows.ToList()) DetachRow(r);
            }

            ReevaluateMoveAllowed();
        }

        private void AttachRow(LogRow? r)
        {
            if (r == null || _trackedRows.Contains(r)) return;
            r.PropertyChanged += Row_PropertyChanged;
            _trackedRows.Add(r);
        }

        private void DetachRow(LogRow? r)
        {
            if (r == null) return;
            if (_trackedRows.Remove(r))
                r.PropertyChanged -= Row_PropertyChanged;
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogRow.Status) || e.PropertyName == nameof(LogRow.PbReason))
                ReevaluateMoveAllowed();
        }

        private void ReevaluateMoveAllowed()
        {
            var allowed = !_trackedRows.Any(r => r.Status == RowStatus.PlannedProblemFolder || !string.IsNullOrWhiteSpace(r.PbReason));
            // Si l'autorisation change et devient false, désactive immédiatement MoveFiles
            if (!allowed && MoveFiles) MoveFiles = false;
            MoveAllowed = allowed;
        }

        private void UpdateCommandStates()
        {
            _scanCmd.RaiseCanExecuteChanged();
            _applyCmd.RaiseCanExecuteChanged();
            _stopCmd.RaiseCanExecuteChanged();
        }

        // Scan autorisé même si TargetFolder non renseigné
        private bool CanScan()
            => !IsBusy
               && Directory.Exists(SourceFolder);

        private bool CanApply()
            => !IsBusy
               && Rows.Any(r => r.Status is RowStatus.OkPlanned or RowStatus.PlannedProblemFolder)
               && Directory.Exists(TargetFolder);

        private void Stop()
        {
            _cts?.Cancel();
            Summary = "Annulation demandée…";
        }

        private async Task ScanAsync()
        {
            Rows.Clear();

            _cts = new CancellationTokenSource();
            IsBusy = true;
            Summary = "Scan en cours…";

            try
            {
                await Task.Run(() => ScanWorker(_cts.Token));
                Summary = BuildSummary("Scan terminé");
            }
            catch (OperationCanceledException)
            {
                Summary = BuildSummary("Scan annulé");
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task ApplyAsync()
        {
            _cts = new CancellationTokenSource();
            IsBusy = true;
            Summary = "Application en cours…";

            try
            {
                await Task.Run(() => ApplyWorker(_cts.Token));
                Summary = BuildSummary("Application terminée");
            }
            catch (OperationCanceledException)
            {
                Summary = BuildSummary("Application annulée");
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // -------------------------
        // Scan rules / constants
        // -------------------------

        private static readonly string[] AudioExts = new[]
        {
            ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wav", ".wma"
        };

        private static readonly string[] SidecarExts = new[]
        {
            ".jpg",".jpeg",".png",".webp",".gif",".bmp",".tif",".tiff",
            ".cue",".log",".m3u",".m3u8",".nfo",".txt",".pdf",
            ".sfv",".md5"
        };

        private const string ProblemsFolderName = "_PROBLEMES";
        private const int TargetPathSoftMax = 245;

        private static bool IsSidecar(string path)
        {
            var ext = Path.GetExtension(path);
            return SidecarExts.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private void ScanWorker(CancellationToken ct)
        {
            var sourceDirs = Directory.EnumerateFiles(SourceFolder, "*.*", SearchOption.AllDirectories)
                .Where(p => AudioExts.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .GroupBy(p => Path.GetDirectoryName(p)!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalRows = new System.Collections.Generic.List<LogRow>();
            var problemFolderRows = new System.Collections.Generic.List<LogRow>();

            foreach (var grp in sourceDirs)
            {
                ct.ThrowIfCancellationRequested();

                var srcDir = grp.Key;

                var problemItems = new System.Collections.Generic.List<(string FileName, string PbContent)>();
                var tmpAudioRows = new System.Collections.Generic.List<LogRow>();

                foreach (var audioPath in grp)
                {
                    ct.ThrowIfCancellationRequested();

                    var row = new LogRow
                    {
                        SourcePath = audioPath,
                        FileName = Path.GetFileName(audioPath), // ajouté
                        Status = RowStatus.Pending,
                        Action = FileAction.None,
                        Timestamp = DateTime.Now
                    };

                    string? albumArtist = null;
                    string? trackArtist = null;
                    string? album = null;
                    string? title = null;
                    uint track = 0;
                    uint disc = 0;
                    uint year = 0;

                    try
                    {
                        using var f = TagLib.File.Create(audioPath);
                        var tag = f.Tag;

                        albumArtist = tag.AlbumArtists?.FirstOrDefault();
                        trackArtist = tag.Performers?.FirstOrDefault();
                        album = tag.Album;
                        title = tag.Title;
                        track = tag.Track;
                        disc = tag.Disc;
                        year = tag.Year;

                        // Règle : AlbumArtist vide -> fallback sur Performers
                        row.Artist = FirstNonEmpty(albumArtist, trackArtist);
                        row.Album = album;
                        row.Title = title;
                        row.Track = track;
                        row.Disc = disc;
                        row.Year = year;

                        // PB: tags bloquants précis
                        var tagPb = GetBlockingTagReason(row);
                        if (tagPb != null)
                        {
                            row.PbReason = tagPb;

                            var pb = BuildPbReport(
                                pbCode: tagPb,
                                stage: "SCAN",
                                sourcePath: audioPath,
                                computedTargetPath: null,
                                albumArtist: albumArtist,
                                trackArtist: trackArtist,
                                album: album,
                                title: title,
                                track: track,
                                disc: disc,
                                year: year,
                                ex: null);

                            problemItems.Add((Path.GetFileName(audioPath), pb));
                            continue;
                        }

                        // Compute target + PB précis (sanitize / path too long / etc.)
                        var targetRes = TryComputeTargetPath(TargetFolder, row);
                        if (targetRes.ProblemCode != null)
                        {
                            row.PbReason = targetRes.ProblemCode;

                            var pb = BuildPbReport(
                                pbCode: targetRes.ProblemCode,
                                stage: "SCAN",
                                sourcePath: audioPath,
                                computedTargetPath: targetRes.ComputedTargetPath,
                                albumArtist: albumArtist,
                                trackArtist: trackArtist,
                                album: album,
                                title: title,
                                track: track,
                                disc: disc,
                                year: year,
                                ex: null);

                            problemItems.Add((Path.GetFileName(audioPath), pb));
                            continue;
                        }

                        row.TargetPath = targetRes.ComputedTargetPath;
                        row.Status = RowStatus.OkPlanned;
                        row.Action = MoveFiles ? FileAction.Move : FileAction.Copy;
                        row.Message = "OK (prévu).";
                        row.PbReason = null;

                        tmpAudioRows.Add(row);
                    }
                    catch (TagLib.CorruptFileException ex)
                    {
                        const string code = "TAGLIB_EXCEPTION:CORRUPT_FILE";
                        row.PbReason = code;

                        var pb = BuildPbReport(code, "SCAN", audioPath, null, albumArtist, trackArtist, album, title, track, disc, year, ex);
                        problemItems.Add((Path.GetFileName(audioPath), pb));
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        const string code = "IO_ERROR:ACCESS_DENIED";
                        row.PbReason = code;

                        var pb = BuildPbReport(code, "SCAN", audioPath, null, albumArtist, trackArtist, album, title, track, disc, year, ex);
                        problemItems.Add((Path.GetFileName(audioPath), pb));
                    }
                    catch (Exception ex)
                    {
                        var code = $"UNEXPECTED_EXCEPTION:{ex.GetType().Name}";
                        row.PbReason = code;

                        var pb = BuildPbReport(code, "SCAN", audioPath, null, albumArtist, trackArtist, album, title, track, disc, year, ex);
                        problemItems.Add((Path.GetFileName(audioPath), pb));
                    }
                }

                // Au moins un audio problématique => dossier complet en _PROBLEMES
                if (problemItems.Count > 0)
                {
                    var folderName = Sanitize(new DirectoryInfo(srcDir).Name);

                    var problemsRoot = Path.Combine(TargetFolder, ProblemsFolderName);
                    var targetDir = Path.Combine(problemsRoot, folderName);

                    var pbText = string.Join(Environment.NewLine, problemItems.Select(x => $"{x.FileName}\t{x.PbContent}"));

                    var folderRow = new LogRow
                    {
                        SourcePath = srcDir,       // dossier
                        FileName = new DirectoryInfo(srcDir).Name, // ajouté : nom du dossier
                        TargetPath = targetDir,    // dossier
                        Status = RowStatus.PlannedProblemFolder,
                        Action = MoveFiles ? FileAction.Move : FileAction.Copy,
                        Message = $"Dossier marqué PROBLÈME ({problemItems.Count} fichier(s) audio).",
                        PbReason = BuildProblemFolderSummary(problemItems),
                        ProblemItems = pbText,
                        Timestamp = DateTime.Now
                    };

                    problemFolderRows.Add(folderRow);
                    continue;
                }

                // Dossier OK => audios + sidecars
                normalRows.AddRange(tmpAudioRows);

                var targetAlbumDir = Path.GetDirectoryName(tmpAudioRows.First().TargetPath!)!;

                foreach (var file in Directory.EnumerateFiles(srcDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();

                    var ext = Path.GetExtension(file);
                    if (AudioExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        continue;
                    if (!IsSidecar(file))
                        continue;

                    normalRows.Add(new LogRow
                    {
                        SourcePath = file,
                        FileName = Path.GetFileName(file), // ajouté
                        TargetPath = Path.Combine(targetAlbumDir, Path.GetFileName(file)),
                        Status = RowStatus.OkPlanned,
                        Action = MoveFiles ? FileAction.Move : FileAction.Copy,
                        Message = "Fichier associé (pochette/sidecar) prévu.",
                        PbReason = null,
                        Timestamp = DateTime.Now
                    });
                }
            }

            App.Current.Dispatcher.Invoke(() =>
            {
                foreach (var r in normalRows) Rows.Add(r);
                foreach (var r in problemFolderRows) Rows.Add(r);
            });
        }

        // -------------------------
        // Apply
        // -------------------------

        private void ApplyWorker(CancellationToken ct)
        {
            foreach (var row in Rows.Where(r => r.Status == RowStatus.OkPlanned).ToList())
            {
                ct.ThrowIfCancellationRequested();
                ApplyFileRow(row);
            }

            foreach (var row in Rows.Where(r => r.Status == RowStatus.PlannedProblemFolder).ToList())
            {
                ct.ThrowIfCancellationRequested();
                ApplyProblemFolderRow(row);
            }
        }

        private void ApplyFileRow(LogRow row)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(row.TargetPath))
                    throw new InvalidOperationException("TargetPath missing.");

                Directory.CreateDirectory(Path.GetDirectoryName(row.TargetPath)!);

                var finalTarget = ResolveFileCollision(row.TargetPath);

                if (MoveFiles)
                    File.Move(row.SourcePath, finalTarget);
                else
                    File.Copy(row.SourcePath, finalTarget, overwrite: false);

                App.Current.Dispatcher.Invoke(() =>
                {
                    row.TargetPath = finalTarget;
                    row.Status = RowStatus.Done;
                    row.Message = MoveFiles ? "Déplacé." : "Copié.";
                    row.PbReason = null;
                    row.Timestamp = DateTime.Now;
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                var code = "IO_ERROR:ACCESS_DENIED";
                WriteApplyPbNextToSource(row.SourcePath, code, row.TargetPath, ex);

                App.Current.Dispatcher.Invoke(() =>
                {
                    row.Status = RowStatus.FailedApply;
                    row.Message = ex.Message;
                    row.PbReason = code;
                    row.Timestamp = DateTime.Now;
                });
            }
            catch (IOException ex)
            {
                var code = "IO_ERROR:IO_EXCEPTION";
                WriteApplyPbNextToSource(row.SourcePath, code, row.TargetPath, ex);

                App.Current.Dispatcher.Invoke(() =>
                {
                    row.Status = RowStatus.FailedApply;
                    row.Message = ex.Message;
                    row.PbReason = code;
                    row.Timestamp = DateTime.Now;
                });
            }
            catch (Exception ex)
            {
                var code = $"UNEXPECTED_EXCEPTION:{ex.GetType().Name}";
                WriteApplyPbNextToSource(row.SourcePath, code, row.TargetPath, ex);

                App.Current.Dispatcher.Invoke(() =>
                {
                    row.Status = RowStatus.FailedApply;
                    row.Message = ex.Message;
                    row.PbReason = code;
                    row.Timestamp = DateTime.Now;
                });
            }
        }

        private void ApplyProblemFolderRow(LogRow row)
        {
            try
            {
                var srcDir = row.SourcePath;
                var tgtDir = row.TargetPath;

                if (string.IsNullOrWhiteSpace(srcDir) || string.IsNullOrWhiteSpace(tgtDir))
                    throw new InvalidOperationException("Problem folder Source/Target missing.");

                if (!Directory.Exists(srcDir))
                    throw new DirectoryNotFoundException($"Source folder not found: {srcDir}");

                var finalTargetDir = ResolveDirectoryCollision(tgtDir);

                if (MoveFiles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(finalTargetDir)!);
                    Directory.Move(srcDir, finalTargetDir);
                }
                else
                {
                    CopyDirectoryRecursive(srcDir, finalTargetDir);
                }

                // Ecrit les pb.txt à côté des fichiers concernés (dans le dossier déplacé/copier)
                WritePbFilesForProblemFolder(row, finalTargetDir);

                App.Current.Dispatcher.Invoke(() =>
                {
                    row.TargetPath = finalTargetDir;
                    row.Status = RowStatus.Done;
                    row.Message = MoveFiles ? "Dossier PROBLÈME déplacé." : "Dossier PROBLÈME copié.";
                    row.Timestamp = DateTime.Now;
                });
            }
            catch (Exception ex)
            {
                var code = $"IO_ERROR:PROBLEM_FOLDER:{ex.GetType().Name}";

                App.Current.Dispatcher.Invoke(() =>
                {
                    row.Status = RowStatus.FailedApply;
                    row.Message = ex.Message;
                    row.PbReason = code;
                    row.Timestamp = DateTime.Now;
                });
            }
        }

        // -------------------------
        // PB codes helpers
        // -------------------------

        private static string? GetBlockingTagReason(LogRow r)
        {
            if (string.IsNullOrWhiteSpace(r.Artist))
                return "MISSING_TAG:ARTIST";
            // album is NOT blocking anymore: if missing, file will be placed directly in the artist folder
            if (string.IsNullOrWhiteSpace(r.Title))
                return "MISSING_TAG:TITLE";
            return null;
        }

        private static string BuildProblemFolderSummary(System.Collections.Generic.List<(string FileName, string PbContent)> problemItems)
        {
            var grouped = problemItems
                .Select(p => (p.PbContent ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault() ?? "UNKNOWN")
                .Select(code => code.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key} ({g.Count()})");

            var summary = string.Join(", ", grouped);
            return string.IsNullOrWhiteSpace(summary)
                ? "HAS_AUDIO_ISSUES"
                : "HAS_AUDIO_ISSUES: " + summary;
        }

        private static bool IsVarious(string? artist)
        {
            if (string.IsNullOrWhiteSpace(artist)) return false;
            var a = artist.Trim().ToLowerInvariant();

            return a is "various"
                or "various artists"
                or "various artist"
                or "va"
                or "v.a."
                or "v.a"
                or "v/a"
                or "compilation"
                or "compilations";
        }

        private readonly struct TargetPathResult
        {
            public TargetPathResult(string? computedTargetPath, string? problemCode)
            {
                ComputedTargetPath = computedTargetPath;
                ProblemCode = problemCode;
            }
            public string? ComputedTargetPath { get; }
            public string? ProblemCode { get; }
        }

        private static TargetPathResult TryComputeTargetPath(string targetRoot, LogRow r)
        {
            var titleRaw = FirstNonEmpty(r.Title) ?? Path.GetFileNameWithoutExtension(r.SourcePath);
            var titleSan = SanitizeWithReason(titleRaw, out var titleReason);
            if (titleReason != null)
                return new TargetPathResult(null, $"TITLE:{titleReason}");

            var ext = Path.GetExtension(r.SourcePath).TrimStart('.').ToLowerInvariant();
            var trackPart = r.Track.ToString("D2");
            var discPrefix = r.Disc > 0 ? $"{r.Disc:D2}-" : "";
            var fileName = $"{discPrefix}{trackPart} - {titleSan}.{ext}";

            var artistRaw = FirstNonEmpty(r.Artist) ?? "";
            var albumRaw = FirstNonEmpty(r.Album) ?? "";

            // Ensure artist is sanitized (artist is still blocking earlier)
            var artistSan = SanitizeWithReason(artistRaw, out var artistReason);
            if (artistReason != null)
                return new TargetPathResult(null, $"ARTIST:{artistReason}");

            // If no album tag -> place track directly under artist folder
            if (string.IsNullOrWhiteSpace(albumRaw))
            {
                var pathNoAlbum = Path.Combine(targetRoot, artistSan, fileName);
                if (pathNoAlbum.Length > TargetPathSoftMax)
                    return new TargetPathResult(pathNoAlbum, $"TARGET_PATH_TOO_LONG:{pathNoAlbum.Length}");
                return new TargetPathResult(pathNoAlbum, null);
            }

            if (IsVarious(artistRaw))
            {
                var albumSan = SanitizeWithReason(albumRaw, out var albumReason);
                if (albumReason != null)
                    return new TargetPathResult(null, $"ALBUM:{albumReason}");

                var path = Path.Combine(targetRoot, albumSan, fileName);
                if (path.Length > TargetPathSoftMax)
                    return new TargetPathResult(path, $"TARGET_PATH_TOO_LONG:{path.Length}");

                return new TargetPathResult(path, null);
            }
            else
            {
                var albumSan = SanitizeWithReason(albumRaw, out var albumReason);
                if (albumReason != null)
                    return new TargetPathResult(null, $"ALBUM:{albumReason}");

                var path = Path.Combine(targetRoot, artistSan, albumSan, fileName);
                if (path.Length > TargetPathSoftMax)
                    return new TargetPathResult(path, $"TARGET_PATH_TOO_LONG:{path.Length}");

                return new TargetPathResult(path, null);
            }
        }

        // -------------------------
        // Char handling: remove listed chars quietly, fail on the rest
        // -------------------------

        // Liste des caractères à supprimer silencieusement (utilisateur demandé)
        private static readonly char[] RemovableFileNameChars = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        private static bool ContainsOtherInvalidChars(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return s.Any(ch => invalid.Contains(ch) && !RemovableFileNameChars.Contains(ch));
        }

        private static string SanitizeWithReason(string input, out string? reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                reason = "EMPTY_VALUE";
                return "Unknown";
            }

            // 1) Supprimer silencieusement les caractères listés par l'utilisateur
            var withoutRemovables = new string(input.Where(ch => !RemovableFileNameChars.Contains(ch)).ToArray()).Trim();

            // 2) Si après suppression il reste des caractères réellement invalides -> erreur
            if (ContainsOtherInvalidChars(withoutRemovables))
            {
                reason = "INVALID_CHARS";
                // Retourner la chaîne nettoyée (même si on signale l'erreur) pour que le code appelant puisse l'afficher/consigner.
                var cleanedPartial = new string(withoutRemovables.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim();
                return string.IsNullOrWhiteSpace(cleanedPartial) ? "Unknown" : cleanedPartial;
            }

            // 3) Normalisation restante: enlever espaces en tête/queue
            var cleaned = withoutRemovables.Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                reason = "EMPTY_AFTER_SANITIZE";
                return "Unknown";
            }

            var reserved = new[] { "CON", "PRN", "AUX", "NUL" };
            if (reserved.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
            {
                reason = "RESERVED_NAME";
                cleaned = $"_{cleaned}_";
            }

            return cleaned;
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static string Sanitize(string input)
        {
            // Méthode de secours utilisée pour dériver un nom de dossier à partir d'un dossier source existant.
            // On supprime ici silencieusement les caractères listés par l'utilisateur et remplace
            // les autres caractères invalides par '_' (usage interne).
            var withoutRemovables = new string(input.Where(ch => !RemovableFileNameChars.Contains(ch)).ToArray());
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(withoutRemovables.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
        }

        // -------------------------
        // PB files content + writing
        // -------------------------

        private static string BuildPbReport(
            string pbCode,
            string stage,
            string sourcePath,
            string? computedTargetPath,
            string? albumArtist,
            string? trackArtist,
            string? album,
            string? title,
            uint track,
            uint disc,
            uint year,
            Exception? ex)
        {
            var sb = new StringBuilder();

            // 1ère ligne = reason (sert pour la colonne PB et le résumé de dossier)
            sb.AppendLine(pbCode);
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Stage: {stage}");
            sb.AppendLine($"SourcePath: {sourcePath}");
            if (!string.IsNullOrWhiteSpace(computedTargetPath))
                sb.AppendLine($"ComputedTargetPath: {computedTargetPath}");

            sb.AppendLine();
            sb.AppendLine("Observed tags:");
            sb.AppendLine($"  AlbumArtist: {Safe(albumArtist)}");
            sb.AppendLine($"  TrackArtist: {Safe(trackArtist)}");
            sb.AppendLine($"  Album: {Safe(album)}");
            sb.AppendLine($"  Title: {Safe(title)}");
            sb.AppendLine($"  Track: {track}");
            sb.AppendLine($"  Disc: {disc}");
            sb.AppendLine($"  Year: {year}");

            if (ex != null)
            {
                sb.AppendLine();
                sb.AppendLine("Exception:");
                sb.AppendLine($"  Type: {ex.GetType().FullName}");
                sb.AppendLine($"  Message: {ex.Message}");
                sb.AppendLine("  StackTrace:");
                sb.AppendLine(Truncate(ex.StackTrace, 2000));
            }

            return sb.ToString();
        }

        private static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? "(empty)" : s;

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(none)";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…(truncated)";
        }

        private static void WritePbFilesForProblemFolder(LogRow folderRow, string finalTargetDir)
        {
            if (string.IsNullOrWhiteSpace(folderRow.ProblemItems))
                return;

            // Chaque ligne: "filename\t<contenu>"
            var lines = folderRow.ProblemItems.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var idx = line.IndexOf('\t');
                if (idx <= 0) continue;

                var fileName = line.Substring(0, idx).Trim();
                var content = line.Substring(idx + 1);

                if (string.IsNullOrWhiteSpace(fileName)) continue;

                var destFilePath = Path.Combine(finalTargetDir, fileName);

                var pbPath = Path.Combine(
                    Path.GetDirectoryName(destFilePath)!,
                    Path.GetFileNameWithoutExtension(destFilePath) + ".pb.txt"
                );

                TryWriteTextFile(pbPath, content);
            }
        }

        private static void WriteApplyPbNextToSource(string sourceFilePath, string pbCode, string? targetPath, Exception ex)
        {
            try
            {
                var content = new StringBuilder();
                content.AppendLine(pbCode);
                content.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                content.AppendLine("Stage: APPLY");
                content.AppendLine($"SourcePath: {sourceFilePath}");
                if (!string.IsNullOrWhiteSpace(targetPath))
                    content.AppendLine($"TargetPath: {targetPath}");
                content.AppendLine();
                content.AppendLine("Exception:");
                content.AppendLine($"  Type: {ex.GetType().FullName}");
                content.AppendLine($"  Message: {ex.Message}");
                content.AppendLine("  StackTrace:");
                content.AppendLine(Truncate(ex.StackTrace, 2000));

                var pbPath = Path.Combine(
                    Path.GetDirectoryName(sourceFilePath)!,
                    Path.GetFileNameWithoutExtension(sourceFilePath) + ".pb.txt"
                );

                TryWriteTextFile(pbPath, content.ToString());
            }
            catch
            {
                // best-effort
            }
        }

        private static void TryWriteTextFile(string path, string content)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
            catch
            {
                // best-effort
            }
        }

        // -------------------------
        // File / directory operations
        // -------------------------

        private static string ResolveFileCollision(string targetPath)
        {
            if (!File.Exists(targetPath))
                return targetPath;

            var dir = Path.GetDirectoryName(targetPath)!;
            var name = Path.GetFileNameWithoutExtension(targetPath);
            var ext = Path.GetExtension(targetPath);

            for (int i = 1; i <= 999; i++)
            {
                var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            throw new IOException("Too many collisions for file.");
        }

        private static string ResolveDirectoryCollision(string targetDir)
        {
            if (!Directory.Exists(targetDir))
                return targetDir;

            var parent = Path.GetDirectoryName(targetDir)!;
            var name = new DirectoryInfo(targetDir).Name;

            for (int i = 1; i <= 999; i++)
            {
                var candidate = Path.Combine(parent, $"{name} ({i})");
                if (!Directory.Exists(candidate))
                    return candidate;
            }

            throw new IOException("Too many collisions for folder.");
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                dest = ResolveFileCollision(dest);
                File.Copy(file, dest, overwrite: false);
            }

            foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
            {
                var destSub = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSub);
            }
        }

        private string BuildSummary(string prefix)
        {
            int total = Rows.Count;
            int ok = Rows.Count(r => r.Status == RowStatus.OkPlanned);
            int pbFolders = Rows.Count(r => r.Status == RowStatus.PlannedProblemFolder);
            int done = Rows.Count(r => r.Status == RowStatus.Done);
            int failed = Rows.Count(r => r.Status == RowStatus.FailedApply);

            return $"{prefix} — Total: {total} | OK: {ok} | Dossiers PROBLÈME: {pbFolders} | Done: {done} | Failed: {failed}";
        }

        // Ajoutez ces membres privés dans la classe MainViewModel (au début de la classe)
        private static readonly char[] AppAllowedFileNameChars = Array.Empty<char>(); // personnalisez si nécessaire
        private static char[] GetEffectiveInvalidFileNameChars()
        {
            var defaultInvalid = Path.GetInvalidFileNameChars();
            if (AppAllowedFileNameChars == null || AppAllowedFileNameChars.Length == 0)
                return defaultInvalid;
            return defaultInvalid.Except(AppAllowedFileNameChars).ToArray();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
