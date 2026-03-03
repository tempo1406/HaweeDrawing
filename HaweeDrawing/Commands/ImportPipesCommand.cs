using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HaweeDrawing.Services;
using Microsoft.Win32;
using System;
using System.Windows;
using WinForms = System.Windows.Forms; // Alias to avoid ambiguity

namespace HaweeDrawing.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportPipesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;
                Document doc = uiDoc.Document;

                if (doc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                var openFileDialog = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    Title = "Select JSON file to import",
                    DefaultExt = "json"
                };

                if (openFileDialog.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                string filePath = openFileDialog.FileName;

                // Ask if user wants to load families from a folder
                string familyDirectory = null;
                var askForFamilies = MessageBox.Show(
                    "Do you want to load .rfa families from a folder?\n\n" +
                    "YES - Select folder containing .rfa files (auto-load missing families)\n" +
                    "NO - Use only families already loaded in the project",
                    "Load Family Files?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (askForFamilies == MessageBoxResult.Yes)
                {
                    using (var folderDialog = new WinForms.FolderBrowserDialog())
                    {
                        folderDialog.Description = "Select folder containing .rfa family files";
                        folderDialog.ShowNewFolderButton = false;

                        if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
                        {
                            familyDirectory = folderDialog.SelectedPath;
                        }
                    }
                }

                var confirmResult = MessageBox.Show(
                    $"This will create pipes and fittings from:\n{filePath}\n\nDo you want to continue?",
                    "Confirm Import",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes)
                {
                    return Result.Cancelled;
                }

                // Create services
                var unitConverter = new UnitConverter();
                var jsonExportService = new JsonExportService();
                var familyLoadService = new FamilyLoadService();
                var pipeCreationService = new PipeCreationServiceEnhanced(unitConverter, familyLoadService);

                var exportData = jsonExportService.ImportData(filePath);

                if (exportData == null || (exportData.Pipes.Count == 0 && exportData.Fittings.Count == 0))
                {
                    MessageBox.Show(
                        "No pipes or fittings found in the JSON file.",
                        "Import Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return Result.Failed;
                }

                var summaryResult = MessageBox.Show(
                    $"Found in JSON:\n" +
                    $"- Pipes: {exportData.Pipes.Count}\n" +
                    $"- Fittings: {exportData.Fittings.Count}\n\n" +
                    $"?? WARNING: If you have already imported this file,\n" +
                    $"you may get duplicate elements!\n\n" +
                    $"Recommendation:\n" +
                    $"- First import: Click YES\n" +
                    $"- Re-import: Delete old elements first\n\n" +
                    $"Continue with import?",
                    "Import Summary",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (summaryResult != MessageBoxResult.Yes)
                {
                    return Result.Cancelled;
                }

                // Create pipes and fittings with auto-connect
                string result = pipeCreationService.CreateFromExportData(doc, exportData, familyDirectory);

                MessageBox.Show(
                    result,
                    "Import Completed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                MessageBox.Show(
                    $"Error importing pipes:\n{ex.Message}",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return Result.Failed;
            }
        }
    }
}
