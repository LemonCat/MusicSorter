using MusicSorter.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace MusicSorter
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private MainViewModel? VM => DataContext as MainViewModel;

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var path = PickFolder("Choisir le dossier source");
            if (!string.IsNullOrWhiteSpace(path) && VM != null)
                VM.SourceFolder = path;
        }

        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            var path = PickFolder("Choisir le dossier cible");
            if (!string.IsNullOrWhiteSpace(path) && VM != null)
                VM.TargetFolder = path;
        }

        private static string? PickFolder(string description)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        private void LogGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            if (grid.SelectedItem is not LogRow row) return;

            var target = row.TargetPath;
            var source = row.SourcePath;

            try
            {
                // Priorité cible
                if (!string.IsNullOrWhiteSpace(target))
                {
                    if (Directory.Exists(target))
                    {
                        OpenExplorer(target, null);
                        return;
                    }

                    var targetDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
                    {
                        if (File.Exists(target))
                        {
                            OpenExplorer(targetDir, target); // select file
                            return;
                        }

                        OpenExplorer(targetDir, null);
                        return;
                    }
                }

                // Fallback source
                if (File.Exists(source))
                {
                    var dir = Path.GetDirectoryName(source);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        OpenExplorer(dir, source);
                }
                else if (Directory.Exists(source))
                {
                    OpenExplorer(source, null);
                }
            }
            catch
            {
                // no crash on dblclick
            }
        }

        private static void OpenExplorer(string folder, string? selectFile)
        {
            if (!string.IsNullOrWhiteSpace(selectFile) && File.Exists(selectFile))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{selectFile}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true
                });
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
