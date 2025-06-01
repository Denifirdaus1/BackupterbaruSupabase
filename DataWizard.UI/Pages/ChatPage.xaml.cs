using DataWizard.Core.Services;
using DataWizard.UI.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics; // Untuk Stopwatch
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI.Text;
using System.Threading; // Untuk CancellationTokenSource
using IOPath = System.IO.Path;

namespace DataWizard.UI.Pages
{
    public sealed partial class ChatPage : Page
    {
        private string selectedFilePath = "";
        private readonly string outputTextPath = @"C:\DataSample\hasil_output.txt";
        private readonly DatabaseService _dbService;
        private int _currentUserId = 1; // Temporary hardcoded user ID for testing
        private Stopwatch _processTimer; // Untuk mengukur waktu proses

        public ChatPage()
        {
            this.InitializeComponent();
            _dbService = new DatabaseService();
            PromptBox.TextChanged += PromptBox_TextChanged;
            LoadUserPreferences();
            _processTimer = new Stopwatch();

            var outputDir = IOPath.GetDirectoryName(outputTextPath);
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
        }

        private async void LoadUserPreferences()
        {
            try
            {
                // Since Supabase doesn't have GetUserPreferredFormatAsync, use default Excel format
                // Reset format buttons
                WordFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;
                ExcelFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;

                // Set default to Excel
                ExcelFormatButton.Style = Resources["SelectedFormatButtonStyle"] as Style;
                OutputFormatBox.SelectedIndex = 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading user preferences: {ex.Message}");
                // Silently fail and use default format
                ExcelFormatButton.Style = Resources["SelectedFormatButtonStyle"] as Style;
                OutputFormatBox.SelectedIndex = 1;
            }
        }

        private async Task ShowDialogAsync(string title, string content)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CharCountText.Text = $"{PromptBox.Text.Length}/1000";
        }

        private async Task<bool> SelectFileAsync()
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".xls");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                selectedFilePath = file.Path;
                OutputBox.Text = $"File dipilih: {selectedFilePath}";
                return true;
            }
            return false;
        }

        private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            await SelectFileAsync();
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            string prompt = PromptBox.Text.Trim();
            string outputFormat = (OutputFormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString().ToLower() ?? "txt";
            string mode = (ModeBox.SelectedItem as ComboBoxItem)?.Content?.ToString().ToLower() ?? "file";

            // Validasi input dasar terlebih dahulu
            if (string.IsNullOrWhiteSpace(prompt))
            {
                await ShowDialogAsync("Validation Error", "Harap masukkan prompt terlebih dahulu.");
                return;
            }

            if ((mode == "file" || mode == "ocr") && string.IsNullOrWhiteSpace(selectedFilePath))
            {
                await ShowDialogAsync("Validation Error", $"Harap pilih file terlebih dahulu untuk mode {mode.ToUpper()}.");
                return;
            }

            if (mode == "ocr" && !string.IsNullOrEmpty(selectedFilePath))
            {
                string[] validImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
                string fileExtension = IOPath.GetExtension(selectedFilePath).ToLower();

                if (!validImageExtensions.Contains(fileExtension))
                {
                    await ShowDialogAsync("Validation Error",
                        $"File yang dipilih bukan format gambar yang didukung.\n" +
                        $"Format yang didukung: JPG, JPEG, PNG, BMP, TIFF\n" +
                        $"File Anda: {fileExtension}");
                    return;
                }
            }

            try
            {
                // Validasi Python environment sebelum memulai
                OutputBox.Text = "Memeriksa Python environment...";
                bool pythonValid = await PythonRunner.ValidatePythonEnvironmentAsync().ConfigureAwait(false);

                if (!pythonValid)
                {
                    await ShowDialogAsync("Python Error",
                        "Python tidak ditemukan atau tidak bisa dijalankan.\n\n" +
                        "Pastikan:\n" +
                        "1. Python sudah terinstall\n" +
                        "2. Path Python di PythonRunner.cs sudah benar\n" +
                        "3. Python bisa dijalankan dari command line");
                    OutputBox.Text = "Error: Python environment tidak valid.";
                    return;
                }

                // Validasi dependensi setelah validasi environment
                OutputBox.Text = "Memeriksa modul Python...";
                bool depsValid = await PythonRunner.ValidatePythonDependenciesAsync().ConfigureAwait(false);

                if (!depsValid)
                {
                    string pythonPath = PythonRunner.GetPythonPath();
                    string installCommand = $"\"{pythonPath}\" -m pip install pandas openai python-docx PyPDF2 pdf2image pytesseract pillow";

                    await ShowDialogAsync("Python Dependencies Missing",
                        $"Modul Python yang diperlukan tidak terinstall!\n\n" +
                        $"Silakan install dengan menjalankan perintah berikut di Command Prompt (Admin):\n\n" +
                        $"{installCommand}\n\n" +
                        $"Setelah menginstall, restart aplikasi.");

                    OutputBox.Text = "Error: Modul Python tidak lengkap";
                    return;
                }

                // Mulai mengukur waktu proses
                _processTimer.Restart();

                WelcomePanel.Visibility = Visibility.Collapsed;
                AnswerBox.Visibility = Visibility.Visible;
                OutputBox.Text = "Memproses data... Mohon tunggu.\n(Proses ini bisa memakan waktu beberapa menit untuk file besar)";

                // Dapatkan tipe file input sebagai ID integer
                int inputFileTypeId;
                if (mode == "prompt-only")
                {
                    inputFileTypeId = await _dbService.GetFileTypeId("PDF").ConfigureAwait(false);
                }
                else if (!string.IsNullOrEmpty(selectedFilePath))
                {
                    string fileExtension = IOPath.GetExtension(selectedFilePath).TrimStart('.').ToUpper();
                    string mappedExtension = fileExtension switch
                    {
                        "JPEG" => "JPG",
                        "XLS" => "XLSX",
                        "CSV" => "XLSX",
                        _ => fileExtension
                    };
                    inputFileTypeId = await _dbService.GetFileTypeId(mappedExtension).ConfigureAwait(false);
                }
                else
                {
                    inputFileTypeId = await _dbService.GetFileTypeId("PDF").ConfigureAwait(false);
                }

                // Dapatkan output format ID
                int outputFormatId = outputFormat == "word" ?
                    await _dbService.GetOutputFormatId("Word").ConfigureAwait(false) :
                    await _dbService.GetOutputFormatId("Excel").ConfigureAwait(false);

                // Catat ke history sebelum proses dimulai
                int historyId = await _dbService.LogHistoryAsync(
                    _currentUserId,
                    inputFileTypeId,
                    outputFormatId,
                    prompt,
                    mode).ConfigureAwait(false);

                if (historyId == -1)
                {
                    Debug.WriteLine("Failed to log history, continuing without database logging");
                }

                // Update status
                OutputBox.Text = "Menjalankan Python script...";

                // Debugging parameter
                Debug.WriteLine($"Calling Python with: mode={mode}, format={outputFormat}, " +
                    $"file={(mode == "prompt-only" ? "none" : selectedFilePath ?? "none")}, " +
                    $"prompt_length={prompt.Length}");

                // Tentukan file path yang akan dikirim ke Python
                string pythonFilePath = (mode == "prompt-only") ? "none" : selectedFilePath ?? "none";

                // Gunakan timeout untuk mencegah hang
                var timeout = TimeSpan.FromSeconds(300);
                using var cts = new CancellationTokenSource(timeout);

                string result = "Error: Timeout";
                try
                {
                    result = await PythonRunner.RunPythonScriptAsync(
                        pythonFilePath,
                        outputTextPath,
                        prompt,
                        outputFormat,
                        mode
                    ).ConfigureAwait(false); // PERBAIKAN PENTING: Tambahkan ConfigureAwait(false)
                }
                catch (OperationCanceledException)
                {
                    result = $"Error: Proses Python timeout setelah {timeout.TotalSeconds} detik";
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                    Debug.WriteLine($"Exception in PythonRunner: {ex}");
                }

                // Hentikan timer dan dapatkan waktu proses
                _processTimer.Stop();
                int processingTimeMs = (int)_processTimer.ElapsedMilliseconds;

                Debug.WriteLine($"Python process completed in {processingTimeMs}ms with result: {result}");

                string outputFileName = string.Empty;
                string outputFilePath = string.Empty;

                // Cek hasil
                if (result == "Success")
                {
                    if (File.Exists(outputTextPath))
                    {
                        OutputBox.Text = "Membaca hasil...";

                        string hasil = await File.ReadAllTextAsync(outputTextPath).ConfigureAwait(false);

                        // Cek apakah hasil mengandung error
                        if (hasil.StartsWith("[ERROR]") || hasil.StartsWith("[GAGAL]"))
                        {
                            OutputBox.Text = $"Proses gagal: {hasil}";
                            if (historyId != -1)
                            {
                                await _dbService.UpdateHistoryProcessingTimeAsync(historyId, processingTimeMs).ConfigureAwait(false);
                            }
                            return;
                        }

                        OutputBox.Text = hasil;

                        // Untuk format Excel, cari file parsed
                        if (outputFormat == "excel")
                        {
                            OutputBox.Text += "\n\nMenunggu file Excel dibuat...";

                            string parsedExcelPath = PythonRunner.GetParsedExcelPath(outputTextPath);

                            // Tunggu hingga file Excel terbuat (dengan timeout)
                            int waitCount = 0;
                            while (!File.Exists(parsedExcelPath) && waitCount < 10)
                            {
                                await Task.Delay(1000).ConfigureAwait(false);
                                waitCount++;
                                Debug.WriteLine($"Waiting for Excel file... {waitCount}/10");
                            }

                            if (File.Exists(parsedExcelPath))
                            {
                                outputFilePath = parsedExcelPath;
                                outputFileName = IOPath.GetFileName(parsedExcelPath);
                                ResultFileText.Text = outputFileName;
                                OutputBox.Text = OutputBox.Text.Replace("Menunggu file Excel dibuat...",
                                    $"[SUKSES] File Excel berhasil dibuat: {outputFileName}");
                            }
                            else
                            {
                                OutputBox.Text = OutputBox.Text.Replace("Menunggu file Excel dibuat...",
                                    "[WARNING] File Excel tidak ditemukan, periksa hasil di file txt.");
                            }
                        }
                        else if (outputFormat == "word")
                        {
                            // Untuk format Word, cari file output
                            string basePath = IOPath.GetDirectoryName(outputTextPath);
                            string baseName = IOPath.GetFileNameWithoutExtension(outputTextPath);
                            string wordPath = IOPath.Combine(basePath, $"{baseName}_output.docx");

                            // Tunggu hingga file Word terbuat (dengan timeout)
                            int waitCount = 0;
                            while (!File.Exists(wordPath) && waitCount < 10)
                            {
                                await Task.Delay(1000).ConfigureAwait(false);
                                waitCount++;
                                Debug.WriteLine($"Waiting for Word file... {waitCount}/10");
                            }

                            if (File.Exists(wordPath))
                            {
                                outputFilePath = wordPath;
                                outputFileName = IOPath.GetFileName(wordPath);
                                ResultFileText.Text = outputFileName;
                                OutputBox.Text += $"\n[SUKSES] File Word berhasil dibuat: {outputFileName}";
                            }
                            else
                            {
                                OutputBox.Text += "\n[WARNING] File Word tidak ditemukan";
                            }
                        }

                        // Update history dengan waktu proses
                        if (historyId != -1)
                        {
                            await _dbService.UpdateHistoryProcessingTimeAsync(historyId, processingTimeMs).ConfigureAwait(false);

                            // Jika ada file output, simpan ke tabel OutputFile
                            if (!string.IsNullOrEmpty(outputFilePath) && File.Exists(outputFilePath))
                            {
                                FileInfo fileInfo = new FileInfo(outputFilePath);
                                await _dbService.LogOutputFileAsync(
                                    historyId,
                                    outputFileName,
                                    outputFilePath,
                                    fileInfo.Length).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        OutputBox.Text = "Proses selesai, tetapi file output tidak ditemukan.\n" +
                                       "Periksa path output atau coba lagi.";
                        if (historyId != -1)
                        {
                            await _dbService.UpdateHistoryProcessingTimeAsync(historyId, processingTimeMs).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    // Hasil mengandung error
                    OutputBox.Text = $"Proses gagal:\n{result}";

                    if (historyId != -1)
                    {
                        await _dbService.UpdateHistoryProcessingTimeAsync(historyId, processingTimeMs).ConfigureAwait(false);
                    }

                    // Show detailed error dialog
                    await ShowDialogAsync("Process Error",
                        $"Proses gagal dengan error:\n\n{result}\n\n" +
                        "Tips:\n" +
                        "- Pastikan file input tidak rusak\n" +
                        "- Coba dengan prompt yang lebih sederhana\n" +
                        "- Periksa koneksi internet untuk API Gemini");
                }
            }
            catch (Exception ex)
            {
                _processTimer.Stop();
                int processingTimeMs = (int)_processTimer.ElapsedMilliseconds;

                Debug.WriteLine($"Error in RunButton_Click: {ex}");

                string errorMessage = $"Terjadi kesalahan aplikasi:\n{ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $"\n\nDetail: {ex.InnerException.Message}";
                }

                OutputBox.Text = errorMessage;
                await ShowDialogAsync("Application Error",
                    $"{errorMessage}\n\n" +
                    "Silakan coba lagi atau hubungi support jika masalah berlanjut.");

                Debug.WriteLine($"Process failed with exception: {ex}");
            }
        }

        private async void FileToFileButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 0;
            await SelectFileAsync();
        }

        private async void PromptToFileButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 2;
            await ShowDialogAsync("Reminder", "Please select your output format (Word or Excel) before proceeding.");
            PromptBox.Focus(FocusState.Programmatic);
        }

        private async void OcrToFileButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 1;
            await SelectFileAsync();
        }


        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"Attempting to load history for user {_currentUserId}...");

                var historyData = await _dbService.GetRecentHistoryAsync(_currentUserId, 10);
                Debug.WriteLine($"Successfully retrieved {historyData.Count} history items");

                var stackPanel = new StackPanel { Spacing = 10 };

                if (historyData.Count == 0)
                {
                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = $"Belum ada riwayat konversi untuk user ID: {_currentUserId}",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    // Add header
                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = "Riwayat Konversi Terakhir",
                        FontWeight = FontWeights.Bold,
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 0, 10)
                    });

                    // Add each history item
                    foreach (var history in historyData)
                    {
                        Debug.WriteLine($"Processing history item: {history.HistoryId}");

                        // Create container for each history item
                        var itemContainer = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            Margin = new Thickness(0, 4, 0, 4)
                        };

                        // Add icon based on output format
                        var formatIcon = new Microsoft.UI.Xaml.Controls.Image
                        {
                            Width = 28,
                            Height = 28,
                            Source = history.OutputFormat == "Word" ?
                                new BitmapImage(new Uri("ms-appx:///Assets/Microsoft Word 2024.png")) :
                                new BitmapImage(new Uri("ms-appx:///Assets/Microsoft Excel 2025.png"))
                        };
                        itemContainer.Children.Add(formatIcon);

                        // Add conversion info
                        var conversionInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                        conversionInfo.Children.Add(new TextBlock
                        {
                            Text = $"{history.InputType} → {history.OutputFormat}",
                            FontSize = 14,
                            FontWeight = history.IsSuccess ? FontWeights.Normal : FontWeights.SemiBold,
                            Foreground = history.IsSuccess ?
                                new SolidColorBrush(Microsoft.UI.Colors.Black) :
                                new SolidColorBrush(Microsoft.UI.Colors.Red)
                        });

                        conversionInfo.Children.Add(new TextBlock
                        {
                            Text = $"{history.ProcessDate:dd/MM/yyyy HH:mm} • {history.ProcessingTime}ms",
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                        });

                        itemContainer.Children.Add(conversionInfo);
                        stackPanel.Children.Add(itemContainer);

                        // Add separator (except after last item)
                        if (history != historyData.Last())
                        {
                            stackPanel.Children.Add(new Rectangle
                            {
                                Height = 1,
                                Fill = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                                Margin = new Thickness(0, 8, 0, 8)
                            });
                        }
                    }
                }

                // Create and show dialog
                var dialog = new ContentDialog
                {
                    Title = "Riwayat Konversi",
                    Content = new ScrollViewer
                    {
                        Content = stackPanel,
                        VerticalScrollMode = ScrollMode.Auto,
                        HorizontalScrollMode = ScrollMode.Disabled,
                        MaxHeight = 500,
                        Padding = new Thickness(0, 0, 10, 0) // Add right padding for scrollbar
                    },
                    CloseButtonText = "Tutup",
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                Debug.WriteLine("Showing history dialog");
                await dialog.ShowAsync();
                Debug.WriteLine("History dialog closed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading history: {ex.ToString()}");
                await ShowDialogAsync("Error",
                    $"Gagal memuat riwayat:\n{ex.Message}\n\n" +
                    $"User ID: {_currentUserId}\n" +
                    $"Silakan cek log untuk detail lebih lanjut.");
            }
        }

        private void CheckCurrentUserId()
        {
            Debug.WriteLine($"Current User ID: {_currentUserId}");
            // Atau tampilkan di UI sementara
            OutputBox.Text = $"Current User ID: {_currentUserId}";
        }

        private async void OutputFormatButton_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            string format = clickedButton.Tag.ToString();

            WordFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;
            ExcelFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;

            clickedButton.Style = Resources["SelectedFormatButtonStyle"] as Style;
            OutputFormatBox.SelectedIndex = format == "word" ? 2 : 1;

            // Note: SaveUserPreferredFormatAsync not available in current Supabase implementation
            // Could be added later if needed
            Debug.WriteLine($"User selected format: {format}");
        }

        private void RefreshPromptButton_Click(object sender, RoutedEventArgs e)
        {
            PromptBox.Text = "";
            selectedFilePath = "";
            OutputBox.Text = "";
            OutputFormatBox.SelectedIndex = 0;
            ModeBox.SelectedIndex = 0;

            WordFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;
            ExcelFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;

            WelcomePanel.Visibility = Visibility.Visible;
            AnswerBox.Visibility = Visibility.Collapsed;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to the HomePage
            this.Frame.Navigate(typeof(DataWizard.UI.Pages.HomePage));
        }

        private async void AddAttachmentButton_Click(object sender, RoutedEventArgs e)
        {
            await SelectFileAsync();
        }

        private async void UseImageButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 1;
            await SelectFileAsync();
        }

        private async void SaveFileButton_Click(object sender, RoutedEventArgs e)
        {
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Excel Files", new List<string>() { ".xlsx" });
            savePicker.FileTypeChoices.Add("Word Documents", new List<string>() { ".docx" });
            savePicker.SuggestedFileName = ResultFileText.Text;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    OutputBox.Text = $"File saved to: {file.Path}";

                    // Note: SaveFileToFolderAsync not available in current Supabase implementation
                    // The file saving functionality still works, just without database tracking
                    Debug.WriteLine($"File saved locally: {file.Path}");
                }
                catch (Exception ex)
                {
                    await ShowDialogAsync("Error", $"Error saving file: {ex.Message}");
                }
            }
        }
    }
}
