using System;

namespace MusicSorter.Models
{
    public class LogRow
    {
        public string? Status { get; set; }
        public string? Action { get; set; }
        public string? PbReason { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Track { get; set; }
        public string? Title { get; set; }

        // NOM DU FICHIER SOURCE demandé : FileName
        public string? FileName { get; set; }

        public string? SourcePath { get; set; }
        public string? TargetPath { get; set; }
        public string? Message { get; set; }
    }
}
