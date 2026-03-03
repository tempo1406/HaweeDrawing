using Autodesk.Revit.UI;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace HaweeDrawing
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string tabName = "Hawee Tools";
                
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch (Exception)
                {
                }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Pipe Export");

                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                PushButtonData exportButtonData = new PushButtonData(
                    "ExportPipes",
                    "Export\nPipes",
                    assemblyPath,
                    "HaweeDrawing.Commands.ExportPipesCommand");

                exportButtonData.ToolTip = "Export piping system to JSON";
                exportButtonData.LongDescription = "Reads all pipes in the current document and exports them to a JSON file with detailed information including diameter, length, connectors, etc.";

                PushButton exportButton = panel.AddItem(exportButtonData) as PushButton;

                PushButtonData importButtonData = new PushButtonData(
                    "ImportPipes",
                    "Import\nPipes",
                    assemblyPath,
                    "HaweeDrawing.Commands.ImportPipesCommand");

                importButtonData.ToolTip = "Import piping system from JSON";
                importButtonData.LongDescription = "Creates pipes and fittings in the current document from a JSON file previously exported.";

                PushButton importButton = panel.AddItem(importButtonData) as PushButton;

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Failed to load add-in: {ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
