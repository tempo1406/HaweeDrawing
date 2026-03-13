using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HaweeDrawing.Services;
using HaweeDrawing.ViewModels;
using HaweeDrawing.Views;
using System;
using System.Windows;

namespace HaweeDrawing.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportPipesCommand : IExternalCommand
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

                var unitConverter = new UnitConverter();
                var pipeCollectorService = new PipeCollectorService(unitConverter);
                var jsonExportService = new JsonExportService();
                var sheetExportService = new SheetExportService();

                var viewModel = new MainViewModel(doc, pipeCollectorService, jsonExportService, sheetExportService);
                var window = new MainWindow
                {
                    DataContext = viewModel
                };

                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
