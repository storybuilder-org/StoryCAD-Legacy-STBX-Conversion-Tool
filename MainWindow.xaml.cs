using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using System.Threading;
using StoryCAD.Services.IoC;
using StoryCAD.DAL;
using Windows.Storage;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
using System.Linq;

namespace LegacySTBXConverter;

public class FileItem
{
    public string Path { get; set; }
    public string Status { get; set; } = "";  // Will show checkmark when converted
}

public sealed partial class MainWindow : Window
{
    private CancellationTokenSource _cancellationTokenSource;
    private ObservableCollection<FileItem> _legacyFileItems = new();
    private int _nonLegacyCount = 0;
    private int _successfulConversions = 0;
    private string _selectedDirectory = "";

    public MainWindow()
    {
        InitializeComponent();
        Title = "StoryCAD Outline Converter";
        LegacyFilesList.ItemsSource = _legacyFileItems;
        
        // Set default directory to Documents/StoryCAD/Projects
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string defaultPath = Path.Combine(documentsPath, "StoryCAD", "Projects");
        if (Directory.Exists(defaultPath))
        {
            DirectoryTextBox.Text = defaultPath;
        }
        
        try
        {
            BootStrapper.Initialise();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initialization warning: {ex.Message}");
        }
        
        // Enable/disable Analyze button based on directory input
        DirectoryTextBox.TextChanged += (s, e) =>
        {
            ConvertButton.IsEnabled = !string.IsNullOrWhiteSpace(DirectoryTextBox.Text) && Directory.Exists(DirectoryTextBox.Text);
        };
        
        // Enable button if default directory is valid
        if (!string.IsNullOrWhiteSpace(DirectoryTextBox.Text) && Directory.Exists(DirectoryTextBox.Text))
        {
            ConvertButton.IsEnabled = true;
        }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker();
        folderPicker.FileTypeFilter.Add("*");
        
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            DirectoryTextBox.Text = folder.Path;
        }
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedDirectory = DirectoryTextBox.Text;
        
        if (string.IsNullOrWhiteSpace(_selectedDirectory) || !Directory.Exists(_selectedDirectory))
        {
            return;
        }

        // Analyze files
        await AnalyzeFiles(_selectedDirectory);
        
        // Show page 2 and slide to it
        Page2.Visibility = Visibility.Visible;
        SlideToPage2.Begin();
        SlideToPage2.Completed += (s, e) => Page1.Visibility = Visibility.Collapsed;
    }

    private async Task AnalyzeFiles(string directory)
    {
        _legacyFileItems.Clear();
        _nonLegacyCount = 0;
        
        await Task.Run(() =>
        {
            var stbxFiles = Directory.GetFiles(directory, "*.stbx", SearchOption.AllDirectories);
            
            foreach (var file in stbxFiles)
            {
                if (IsLegacyXmlFile(file))
                {
                    var relativePath = Path.GetRelativePath(directory, file);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _legacyFileItems.Add(new FileItem { Path = relativePath });
                    });
                }
                else
                {
                    _nonLegacyCount++;
                }
            }
        });
        
        // Update UI based on results
        if (_legacyFileItems.Count == 0)
        {
            NoLegacyFilesText.Visibility = Visibility.Visible;
            StartConversionButton.IsEnabled = false;
            FileCountText.Text = $"{_nonLegacyCount} modern outline files found (already up to date)";
        }
        else
        {
            NoLegacyFilesText.Visibility = Visibility.Collapsed;
            StartConversionButton.IsEnabled = true;
            FileCountText.Text = $"{_legacyFileItems.Count} outline files to update | {_nonLegacyCount} already current";
        }
    }

    private async void StartConversion_Click(object sender, RoutedEventArgs e)
    {
        if (_legacyFileItems.Count == 0)
        {
            // If no files to convert, go straight to completion
            CompletionSummaryText.Text = $"No updates needed!\n{_nonLegacyCount} outline files are already current";
            Page3.Visibility = Visibility.Visible;
            SlideToPage3.Begin();
            SlideToPage3.Completed += (s, e) => Page2.Visibility = Visibility.Collapsed;
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        
        StartConversionButton.IsEnabled = false;
        BackToPage1Button.IsEnabled = false;
        ConversionProgressBar.Visibility = Visibility.Visible;
        ConversionProgressBar.Maximum = _legacyFileItems.Count;
        ConversionProgressBar.Value = 0;
        _successfulConversions = 0;
        
        try
        {
            await ConvertFiles();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Conversion error: {ex.Message}");
        }
        finally
        {
            ConversionProgressBar.Visibility = Visibility.Collapsed;
            
            // Update completion summary
            if (_successfulConversions == _legacyFileItems.Count)
            {
                CompletionSummaryText.Text = $"Successfully updated {_successfulConversions} outline files\n{_nonLegacyCount} were already current";
            }
            else
            {
                var failed = _legacyFileItems.Count - _successfulConversions;
                CompletionSummaryText.Text = $"Updated {_successfulConversions} outline files\n{failed} could not be updated\n{_nonLegacyCount} were already current";
            }
            
            // Slide to completion page
            await Task.Delay(500); // Brief pause to see final status
            Page3.Visibility = Visibility.Visible;
            SlideToPage3.Begin();
            SlideToPage3.Completed += (s, e) => Page2.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ConvertFiles()
    {
        for (int i = 0; i < _legacyFileItems.Count; i++)
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                break;
            
            var item = _legacyFileItems[i];
            var fullPath = Path.Combine(_selectedDirectory, item.Path);
            
            try
            {
                await ConvertFile(fullPath);
                
                // Update UI to show checkmark
                DispatcherQueue.TryEnqueue(() =>
                {
                    item.Status = "\uE73E"; // Checkmark icon
                    _successfulConversions++;
                    ConversionProgressBar.Value = i + 1;
                });
            }
            catch (Exception ex)
            {
                // Update UI to show error
                DispatcherQueue.TryEnqueue(() =>
                {
                    item.Status = "\uE711"; // X icon
                    ConversionProgressBar.Value = i + 1;
                });
                Debug.WriteLine($"Failed to convert {item.Path}: {ex.Message}");
            }
        }
    }

    private async Task ConvertFile(string filePath)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
        var story = await new StoryIO().ReadStory(file);
        if (story == null)
        {
            throw new Exception("Failed to read file");
        }
        await new StoryIO().WriteStory(filePath, story);
    }

    private bool IsLegacyXmlFile(string filePath)
    {
        try
        {
            using (var stream = File.OpenRead(filePath))
            {
                if (stream.Length == 0)
                    return false;
                
                byte[] buffer = new byte[Math.Min(100, stream.Length)];
                stream.Read(buffer, 0, buffer.Length);
                string content = System.Text.Encoding.UTF8.GetString(buffer).Trim();
                
                // XML files start with <?xml or <
                // JSON files start with { or [
                if (content.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || 
                    (content.StartsWith("<") && !content.StartsWith("<!")))
                {
                    return true; // It's XML (legacy)
                }
                else if (content.StartsWith("{") || content.StartsWith("["))
                {
                    return false; // It's JSON (modern)
                }
                
                return true; // If uncertain, assume it needs conversion
            }
        }
        catch
        {
            return true; // If we can't read it, assume it needs conversion
        }
    }

    private void BackToPage1_Click(object sender, RoutedEventArgs e)
    {
        Page1.Visibility = Visibility.Visible;
        SlideBackToPage1.Begin();
        SlideBackToPage1.Completed += (s, ev) => Page2.Visibility = Visibility.Collapsed;
        
        // Reset for next use
        _legacyFileItems.Clear();
        DirectoryTextBox.Text = "";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Close the application
        Application.Current.Exit();
    }

    private void ConvertMore_Click(object sender, RoutedEventArgs e)
    {
        // Reset everything and go back to page 1
        _legacyFileItems.Clear();
        DirectoryTextBox.Text = "";
        _nonLegacyCount = 0;
        _successfulConversions = 0;
        
        // Reset visibility and opacity
        Page1.Visibility = Visibility.Visible;
        Page1.Opacity = 1;
        Page2.Visibility = Visibility.Collapsed;
        Page2.Opacity = 0;
        Page3.Visibility = Visibility.Collapsed;
        Page3.Opacity = 0;
    }
}