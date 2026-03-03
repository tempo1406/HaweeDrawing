using HaweeDrawing.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace HaweeDrawing.Services
{
    public interface IJsonExportService
    {
        void ExportData(ExportDataModel exportData, string filePath);
        ExportDataModel ImportData(string filePath);
        void ExportPipes(List<PipeModel> pipes, string filePath);
        void ExportSystems(List<SystemModel> systems, string filePath);
    }

    public class JsonExportService : IJsonExportService
    {
        public void ExportData(ExportDataModel exportData, string filePath)
        {
            if (exportData == null)
                throw new ArgumentNullException(nameof(exportData));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            var json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public ExportDataModel ImportData(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("JSON file not found", filePath);

            var json = File.ReadAllText(filePath);
            var exportData = JsonConvert.DeserializeObject<ExportDataModel>(json);

            if (exportData == null)
                throw new InvalidOperationException("Failed to deserialize JSON data");

            return exportData;
        }

        public void ExportPipes(List<PipeModel> pipes, string filePath)
        {
            if (pipes == null)
                throw new ArgumentNullException(nameof(pipes));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            var json = JsonConvert.SerializeObject(pipes, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public void ExportSystems(List<SystemModel> systems, string filePath)
        {
            if (systems == null)
                throw new ArgumentNullException(nameof(systems));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            var json = JsonConvert.SerializeObject(systems, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }
    }
}
