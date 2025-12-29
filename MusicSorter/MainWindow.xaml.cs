using MusicSorter.ViewModels;
using MusicSorter.Models;
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
            // Le DataGrid est lié à ViewModels.LogRow, pas Models.LogRow.
            if (grid.SelectedItem is not MusicSorter.ViewModels.LogRow row) return;

            try
            {
                // Priorité cible
                if (!string.IsNullOrWhiteSpace(row.TargetPath))
                {
                    // Si TargetPath est un dossier, ouvrir le dossier
                    if (Directory.Exists(row.TargetPath))
                    {
                        OpenExplorer(row.TargetPath, null);
                        return;
                    }

                    // Sinon tenter d'obtenir le dossier parent
                    var targetDir = Path.GetDirectoryName(row.TargetPath);
                    if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
                    {
                        // Si le fichier cible existe, l'ouvrir en le sélectionnant
                        if (File.Exists(row.TargetPath))
                        {
                            OpenExplorer(targetDir, row.TargetPath); // select file
                            return;
                        }

                        // Si le fichier n'existe pas mais qu'on connaît le nom attendu, demander la sélection du nom dans le dossier
                        var expectedFileName = Path.GetFileName(row.TargetPath);
                        if (!string.IsNullOrWhiteSpace(expectedFileName))
                        {
                            var candidate = Path.Combine(targetDir, expectedFileName);
                            OpenExplorer(targetDir, candidate); // tentera la sélection (selon comportement de l'explorer)
                            return;
                        }

                        // Sinon ouvrir simplement le dossier parent
                        OpenExplorer(targetDir, null);
                        return;
                    }
                }

                // Fallback source
                if (!string.IsNullOrWhiteSpace(row.SourcePath))
                {
                    if (File.Exists(row.SourcePath))
                    {
                        var dir = Path.GetDirectoryName(row.SourcePath);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                            OpenExplorer(dir, row.SourcePath);
                    }
                    else if (Directory.Exists(row.SourcePath))
                    {
                        OpenExplorer(row.SourcePath, null);
                    }
                }
            }
            catch
            {
                // no crash on dblclick
            }
        }

        private static void OpenExplorer(string folder, string? selectFile)
        {
            if (!string.IsNullOrWhiteSpace(selectFile))
            {
                // Use /select,"path" to ask Explorer to select the item
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

        private void DataGridRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Implémentez ici le comportement souhaité lors du clic sur une ligne du DataGrid
            // Par exemple, vous pouvez récupérer la ligne sélectionnée :
            // var row = (DataGridRow)sender;
            // var item = row.Item;
        }
    }
}
