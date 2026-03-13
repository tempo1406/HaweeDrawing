using Autodesk.Revit.DB;
using HaweeDrawing.Commands;
using HaweeDrawing.Models;
using HaweeDrawing.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace HaweeDrawing.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly Document _document;
        private readonly IPipeCollectorService _pipeCollectorService;
        private readonly IJsonExportService _jsonExportService;
        private readonly ISheetExportService _sheetExportService;

        private ObservableCollection<PipeModel> _pipes;
        private ObservableCollection<string> _systemNames;
        private ObservableCollection<string> _levelNames;
        private string _selectedSystemName;
        private string _selectedLevelName;
        private string _exportFilePath;
        private int _totalPipes;
        private bool _isLoading;
        private bool _filterByActiveView;

        public MainViewModel(
            Document document,
            IPipeCollectorService pipeCollectorService,
            IJsonExportService jsonExportService,
            ISheetExportService sheetExportService)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _pipeCollectorService = pipeCollectorService ?? throw new ArgumentNullException(nameof(pipeCollectorService));
            _jsonExportService = jsonExportService ?? throw new ArgumentNullException(nameof(jsonExportService));
            _sheetExportService = sheetExportService ?? throw new ArgumentNullException(nameof(sheetExportService));

            Pipes = new ObservableCollection<PipeModel>();
            SystemNames = new ObservableCollection<string>();
            LevelNames = new ObservableCollection<string>();

            ExportCommand = new RelayCommand(_ => ExportToJson(), _ => CanExport());
            ExportSheetsCommand = new RelayCommand(_ => ExportSheetsByLevelAndSystem(), _ => CanExportSheets());
            BrowseCommand = new RelayCommand(_ => BrowseFilePath());

            Initialize();
        }

        #region Properties

        public ObservableCollection<PipeModel> Pipes
        {
            get => _pipes;
            set => SetProperty(ref _pipes, value);
        }

        public ObservableCollection<string> SystemNames
        {
            get => _systemNames;
            set => SetProperty(ref _systemNames, value);
        }

        public ObservableCollection<string> LevelNames
        {
            get => _levelNames;
            set => SetProperty(ref _levelNames, value);
        }

        public string SelectedSystemName
        {
            get => _selectedSystemName;
            set
            {
                if (SetProperty(ref _selectedSystemName, value))
                {
                    LoadPipes();
                }
            }
        }

        public string SelectedLevelName
        {
            get => _selectedLevelName;
            set
            {
                if (SetProperty(ref _selectedLevelName, value))
                {
                    LoadPipes();
                }
            }
        }

        public string ExportFilePath
        {
            get => _exportFilePath;
            set => SetProperty(ref _exportFilePath, value);
        }

        public int TotalPipes
        {
            get => _totalPipes;
            set => SetProperty(ref _totalPipes, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool FilterByActiveView
        {
            get => _filterByActiveView;
            set
            {
                if (SetProperty(ref _filterByActiveView, value))
                {
                    LoadPipes();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand ExportCommand { get; }
        public ICommand ExportSheetsCommand { get; }
        public ICommand BrowseCommand { get; }

        #endregion

        #region Methods

        private void Initialize()
        {
            try
            {
                IsLoading = true;

                var systemNames = _pipeCollectorService.GetSystemNames(_document);
                SystemNames.Clear();
                SystemNames.Add("All Systems");
                foreach (var name in systemNames)
                {
                    SystemNames.Add(name);
                }

                var levelNames = _pipeCollectorService.GetLevelNames(_document);
                LevelNames.Clear();
                LevelNames.Add("All Levels");
                foreach (var name in levelNames)
                {
                    LevelNames.Add(name);
                }

                SelectedSystemName = "All Systems";
                SelectedLevelName = "All Levels";
                ExportFilePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"PipeExport_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                LoadPipes();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExportSheetsByLevelAndSystem()
        {
            try
            {
                IsLoading = true;

                var systemFilter = SelectedSystemName == "All Systems" ? null : SelectedSystemName;
                var levelFilter = SelectedLevelName == "All Levels" ? null : SelectedLevelName;

                var exportData = _pipeCollectorService.GetExportData(
                    _document,
                    FilterByActiveView,
                    systemFilter,
                    levelFilter);
                var groupedPipes = exportData.Pipes
                    .GroupBy(p => new
                    {
                        Level = string.IsNullOrWhiteSpace(p.LevelName) ? "NoLevel" : p.LevelName,
                        System = string.IsNullOrWhiteSpace(p.SystemTypeName) ? "NoSystem" : p.SystemTypeName
                    })
                    .ToList();

                if (groupedPipes.Count == 0)
                {
                    MessageBox.Show("No pipes found to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int createdCount = _sheetExportService.ExportSheetsByLevelAndSystem(_document, exportData.Pipes);

                MessageBox.Show(
                    $"Ð? t?o {createdCount} sheet trong Revit (g?m sheet thông tin + sheet k? thu?t cho t?ng Level/System) và ð? fit viewport trong kh? sheet.",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting sheets: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanExportSheets()
        {
            return !IsLoading && Pipes.Count > 0;
        }

        private void LoadPipes()
        {
            try
            {
                IsLoading = true;

                List<PipeModel> pipes;

                if (FilterByActiveView)
                {
                    pipes = _pipeCollectorService.GetPipesInActiveView(_document);
                }
                else
                {
                    pipes = _pipeCollectorService.GetAllPipes(_document);
                }

                if (!string.IsNullOrEmpty(SelectedSystemName) && SelectedSystemName != "All Systems")
                {
                    pipes = pipes.Where(p => p.SystemTypeName == SelectedSystemName).ToList();
                }

                if (!string.IsNullOrEmpty(SelectedLevelName) && SelectedLevelName != "All Levels")
                {
                    pipes = pipes.Where(p => p.LevelName == SelectedLevelName).ToList();
                }

                Pipes.Clear();
                foreach (var pipe in pipes)
                {
                    Pipes.Add(pipe);
                }

                TotalPipes = Pipes.Count;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading pipes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanExport()
        {
            return !IsLoading && Pipes.Count > 0 && !string.IsNullOrEmpty(ExportFilePath);
        }

        private void ExportToJson()
        {
            try
            {
                IsLoading = true;

                var systemFilter = SelectedSystemName == "All Systems" ? null : SelectedSystemName;
                var levelFilter = SelectedLevelName == "All Levels" ? null : SelectedLevelName;

                var exportData = _pipeCollectorService.GetExportData(
                    _document, 
                    FilterByActiveView, 
                    systemFilter,
                    levelFilter);

                _jsonExportService.ExportData(exportData, ExportFilePath);

                MessageBox.Show(
                    $"Successfully exported {exportData.Pipes.Count} pipes and {exportData.Fittings.Count} fittings to:\n{ExportFilePath}",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to JSON: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void BrowseFilePath()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"PipeExport_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                ExportFilePath = dialog.FileName;
            }
        }

        #endregion
    }
}
