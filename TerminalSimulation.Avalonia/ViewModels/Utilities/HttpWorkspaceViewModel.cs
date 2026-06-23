using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TerminalSimulation.Avalonia.ViewModels.Utilities
{
    public partial class HttpWorkspaceViewModel : ObservableObject
    {
        public ObservableCollection<HttpFolderViewModel> Folders { get; } = new();

        [ObservableProperty] private HttpRequesterViewModel? _activeRequest;

        partial void OnActiveRequestChanged(HttpRequesterViewModel? value)
        {
            foreach (var folder in Folders)
            {
                foreach (var req in folder.Requests)
                {
                    req.IsSelected = req == value;
                }
            }
        }

        private readonly string _workspaceFilePath;

        public HttpWorkspaceViewModel()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerminalSimulation");
            Directory.CreateDirectory(appDataDir);
            _workspaceFilePath = Path.Combine(appDataDir, "http_workspace.json");

            LoadFromDisk();

            if (Folders.Count == 0)
            {
                // Create a default folder
                var defaultFolder = new HttpFolderViewModel { Name = "默认分组" };
                var defaultReq = new HttpRequesterViewModel { Name = "新建请求" };
                defaultFolder.Requests.Add(defaultReq);
                Folders.Add(defaultFolder);
                ActiveRequest = defaultReq;
            }
            else
            {
                ActiveRequest = Folders.FirstOrDefault()?.Requests.FirstOrDefault();
            }
        }

        [RelayCommand]
        private void AddFolder()
        {
            Folders.Add(new HttpFolderViewModel { Name = "新分组" });
            SaveToDisk();
        }

        [RelayCommand]
        private void AddRequest(HttpFolderViewModel folder)
        {
            if (folder == null) return;
            var req = new HttpRequesterViewModel { Name = "新请求" };
            folder.Requests.Add(req);
            ActiveRequest = req;
            folder.IsExpanded = true;
            SaveToDisk();
        }

        [RelayCommand]
        private void RemoveFolder(HttpFolderViewModel folder)
        {
            if (folder == null) return;
            Folders.Remove(folder);
            if (ActiveRequest != null && !Folders.SelectMany(f => f.Requests).Contains(ActiveRequest))
            {
                ActiveRequest = Folders.FirstOrDefault()?.Requests.FirstOrDefault();
            }
            SaveToDisk();
        }

        [RelayCommand]
        private void RemoveRequest(HttpRequesterViewModel req)
        {
            if (req == null) return;
            foreach (var folder in Folders)
            {
                if (folder.Requests.Contains(req))
                {
                    folder.Requests.Remove(req);
                    break;
                }
            }
            if (ActiveRequest == req)
            {
                ActiveRequest = Folders.FirstOrDefault()?.Requests.FirstOrDefault();
            }
            SaveToDisk();
        }

        [RelayCommand]
        private void SelectRequest(HttpRequesterViewModel req)
        {
            if (req != null)
            {
                ActiveRequest = req;
            }
        }

        [RelayCommand]
        private async Task ExportWorkspace()
        {
            try
            {
                var topLevel = global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                if (topLevel == null) return;
                
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "导出工作区",
                    DefaultExtension = "json",
                    SuggestedFileName = "http_workspace_export.json"
                });

                if (file != null)
                {
                    var json = File.ReadAllText(_workspaceFilePath);
                    await using var stream = await file.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ImportWorkspace()
        {
            try
            {
                var topLevel = global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "导入工作区",
                    AllowMultiple = false
                });

                if (files != null && files.Count > 0)
                {
                    var file = files[0];
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
                    
                    File.WriteAllText(_workspaceFilePath, json);
                    LoadFromDisk();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
            }
        }

        [RelayCommand]
        public void SaveToDisk()
        {
            try
            {
                // We should only serialize necessary data. For now, we can use System.Text.Json. 
                // To avoid serializing complex viewmodel states (like actual responses, commands), 
                // it might be better to create a simpler DTO. But for quick iteration, we'll try to serialize the VMs directly 
                // and ignore certain properties. 
                // Actually, since the ViewModels have many properties, we'll write a simple save/load logic for essential fields.
                
                var data = Folders.Select(f => new 
                {
                    f.Id,
                    f.Name,
                    f.IsExpanded,
                    Requests = f.Requests.Select(r => new 
                    {
                        r.Id,
                        r.Name,
                        r.Method,
                        r.Url,
                        Params = r.Params.Select(p => new { p.IsEnabled, p.Key, p.Value }).ToList(),
                        Headers = r.Headers.Select(h => new { h.IsEnabled, h.Key, h.Value }).ToList(),
                        r.BodyTypeIndex,
                        r.RawBodyTypeIndex,
                        r.RawBody,
                        FormData = r.FormData.Select(fd => new { fd.IsEnabled, fd.Key, fd.Value }).ToList(),
                        FormUrlEncodedData = r.FormUrlEncodedData.Select(fd => new { fd.IsEnabled, fd.Key, fd.Value }).ToList()
                    }).ToList()
                }).ToList();

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_workspaceFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save workspace: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_workspaceFilePath)) return;

                var json = File.ReadAllText(_workspaceFilePath);
                var data = JsonSerializer.Deserialize<System.Collections.Generic.List<FolderDto>>(json);
                if (data == null) return;

                Folders.Clear();
                foreach (var f in data)
                {
                    var folderVm = new HttpFolderViewModel 
                    { 
                        Id = f.Id ?? Guid.NewGuid().ToString("N"), 
                        Name = f.Name ?? "分组", 
                        IsExpanded = f.IsExpanded 
                    };
                    
                    if (f.Requests != null)
                    {
                        foreach (var r in f.Requests)
                        {
                            var reqVm = new HttpRequesterViewModel
                            {
                                Id = r.Id ?? Guid.NewGuid().ToString("N"),
                                Name = r.Name ?? "请求",
                                Method = r.Method ?? "GET",
                                Url = r.Url ?? "",
                                BodyTypeIndex = r.BodyTypeIndex,
                                RawBodyTypeIndex = r.RawBodyTypeIndex,
                                RawBody = r.RawBody ?? ""
                            };

                            if (r.Params != null)
                            {
                                reqVm.Params.Clear();
                                foreach (var p in r.Params) reqVm.Params.Add(new HttpKeyValueItem { IsEnabled = p.IsEnabled, Key = p.Key, Value = p.Value });
                            }
                            if (r.Headers != null)
                            {
                                reqVm.Headers.Clear();
                                foreach (var h in r.Headers) reqVm.Headers.Add(new HttpKeyValueItem { IsEnabled = h.IsEnabled, Key = h.Key, Value = h.Value });
                            }
                            if (r.FormData != null)
                            {
                                reqVm.FormData.Clear();
                                foreach (var fd in r.FormData) reqVm.FormData.Add(new HttpKeyValueItem { IsEnabled = fd.IsEnabled, Key = fd.Key, Value = fd.Value });
                            }
                            if (r.FormUrlEncodedData != null)
                            {
                                reqVm.FormUrlEncodedData.Clear();
                                foreach (var fd in r.FormUrlEncodedData) reqVm.FormUrlEncodedData.Add(new HttpKeyValueItem { IsEnabled = fd.IsEnabled, Key = fd.Key, Value = fd.Value });
                            }

                            folderVm.Requests.Add(reqVm);
                        }
                    }
                    Folders.Add(folderVm);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load workspace: {ex.Message}");
            }
        }

        // DTOs for deserialization
        private class FolderDto
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public bool IsExpanded { get; set; }
            public System.Collections.Generic.List<RequestDto>? Requests { get; set; }
        }

        private class RequestDto
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Method { get; set; }
            public string? Url { get; set; }
            public int BodyTypeIndex { get; set; }
            public int RawBodyTypeIndex { get; set; }
            public string? RawBody { get; set; }
            public System.Collections.Generic.List<KvDto>? Params { get; set; }
            public System.Collections.Generic.List<KvDto>? Headers { get; set; }
            public System.Collections.Generic.List<KvDto>? FormData { get; set; }
            public System.Collections.Generic.List<KvDto>? FormUrlEncodedData { get; set; }
        }

        private class KvDto
        {
            public bool IsEnabled { get; set; }
            public string? Key { get; set; }
            public string? Value { get; set; }
        }
    }
}
