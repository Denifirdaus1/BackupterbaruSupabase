using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Text;
using System.Threading;

namespace DataWizard.Core.Services
{
    public static class PythonRunner
    {
        private static readonly string pythonExePath = @"C:\Users\Lenovo\AppData\Local\Programs\Python\Python313\python.exe";
        private static readonly string scriptPath = @"C:\Project PBTGM\PythonEngine\main.py";
        private static readonly int timeoutSeconds = 300; // 5 menit timeout

        public static async Task<string> RunPythonScriptAsync(string filePath, string outputTxtPath, string prompt, string outputFormat, string mode)
        {
            Debug.WriteLine($"Menggunakan Tesseract di: C:\\Program Files\\Tesseract-OCR\\tesseract.exe");

            // Validasi path Python dan script
            if (!File.Exists(pythonExePath))
            {
                return $"Error: Python executable tidak ditemukan di {pythonExePath}. " +
                       "Pastikan Python terinstall dengan benar.";
            }

            if (!File.Exists(scriptPath))
            {
                return $"Error: Python script tidak ditemukan di {scriptPath}. " +
                       "Pastikan file main.py ada di lokasi yang tepat.";
            }

            // Buat direktori output jika belum ada
            string outputDir = Path.GetDirectoryName(outputTxtPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                try
                {
                    Directory.CreateDirectory(outputDir);
                    Debug.WriteLine($"Created output directory: {outputDir}");
                }
                catch (Exception ex)
                {
                    return $"Error: Tidak bisa membuat direktori output: {ex.Message}";
                }
            }

            // Escape dan encode parameter untuk menghindari masalah karakter khusus
            string escapedPrompt = EscapeArgument(prompt);
            string escapedFilePath = EscapeArgument(filePath);
            string escapedOutputPath = EscapeArgument(outputTxtPath);

            var psi = new ProcessStartInfo
            {
                FileName = pythonExePath,
                Arguments = $"\"{scriptPath}\" {escapedFilePath} {escapedOutputPath} {escapedPrompt} \"{outputFormat}\" \"{mode}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            string result = "";
            string output = "";
            string error = "";

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.EnableRaisingEvents = true; // Penting untuk async

                    // TaskCompletionSource untuk menunggu proses selesai
                    var tcs = new TaskCompletionSource<bool>();

                    // Handle process exit event
                    process.Exited += (sender, args) => {
                        tcs.TrySetResult(true);
                        process.WaitForExit(); // Pastikan proses benar-benar selesai
                    };

                    // Debug logging
                    Debug.WriteLine($"Starting Python process with arguments: {psi.Arguments}");

                    if (!process.Start())
                    {
                        return "Error: Gagal memulai proses Python";
                    }

                    Debug.WriteLine($"Python process started (PID: {process.Id})");

                    // Baca output dan error secara async
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    // Buat task timeout
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

                    // Tunggu proses selesai atau timeout
                    var completedTask = await Task.WhenAny(
                        tcs.Task,
                        timeoutTask
                    );

                    // Handle timeout
                    if (completedTask == timeoutTask)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                Debug.WriteLine($"Python process killed due to timeout");
                                return $"Error: Proses Python timeout setelah {timeoutSeconds} detik";
                            }
                        }
                        catch (Exception killEx)
                        {
                            Debug.WriteLine($"Error killing process: {killEx.Message}");
                            return "Error: Proses Python hang dan tidak bisa dihentikan.";
                        }
                    }

                    // Pastikan proses benar-benar keluar
                    if (!process.HasExited)
                    {
                        process.WaitForExit(5000); // Tunggu tambahan 5 detik
                    }

                    // Ambil output dan error
                    output = await outputTask;
                    error = await errorTask;

                    Debug.WriteLine($"Python process exit code: {process.ExitCode}");
                    Debug.WriteLine($"Python output length: {output?.Length ?? 0} characters");
                    Debug.WriteLine($"Python error length: {error?.Length ?? 0} characters");

                    // Periksa exit code
                    if (process.ExitCode != 0)
                    {
                        return $"Error: Python script gagal dengan exit code {process.ExitCode}\n" +
                               $"Error: {error}\n" +
                               $"Output: {output}";
                    }

                    // Periksa apakah ada error dalam output
                    if (!string.IsNullOrEmpty(error) && !error.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Error: {error}";
                    }

                    // Periksa output untuk menentukan sukses
                    if (output.Contains("OK") || output.Contains("[SUKSES]"))
                    {
                        result = "Success";
                    }
                    else if (output.Contains("[ERROR]") || output.Contains("[GAGAL]"))
                    {
                        result = $"Error: {output}";
                    }
                    else
                    {
                        // Jika tidak ada indikator jelas, periksa apakah file output berhasil dibuat
                        if (File.Exists(outputTxtPath))
                        {
                            string fileContent = await File.ReadAllTextAsync(outputTxtPath);
                            if (!string.IsNullOrWhiteSpace(fileContent) &&
                                !fileContent.StartsWith("[ERROR]") &&
                                !fileContent.StartsWith("[GAGAL]"))
                            {
                                result = "Success";
                            }
                            else
                            {
                                result = $"Error: Output file kosong atau berisi error: {fileContent}";
                            }
                        }
                        else
                        {
                            result = $"Error: File output tidak dibuat.\nOutput: {output}\nError: {error}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result = $"Error: Exception saat menjalankan Python script: {ex.Message}";
                Debug.WriteLine($"Exception in RunPythonScriptAsync: {ex}");
            }

            return result;
        }

        public static string GetPythonPath()
        {
            return pythonExePath;
        }

        private static string EscapeArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return "\"\"";

            // Escape double quotes dan backslashes
            string escaped = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");

            // Wrap dalam quotes jika mengandung spasi atau karakter khusus
            if (escaped.Contains(" ") || escaped.Contains("\t") || escaped.Contains("\n"))
            {
                escaped = $"\"{escaped}\"";
            }

            return escaped;
        }

        public static string GetParsedExcelPath(string outputTxtPath)
        {
            return outputTxtPath.Replace(".txt", "_parsed.xlsx");
        }

        // Method untuk validasi dan cek path Python
        public static async Task<bool> ValidatePythonEnvironmentAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();

                    var output = await process.StandardOutput.ReadToEndAsync();
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        await process.WaitForExitAsync(cts.Token); // ✅ FIXED
                    }


                    return process.ExitCode == 0 && output.Contains("Python");
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> ValidatePythonDependenciesAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExePath,
                    Arguments = "-c \"import pandas, openai, docx, PyPDF2, pytesseract; from PIL import Image; print('OK')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();

                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();

                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        await process.WaitForExitAsync(cts.Token); // ✅ FIXED
                    }


                    Debug.WriteLine($"Dependencies check output: {output}");
                    Debug.WriteLine($"Dependencies check error: {error}");

                    return process.ExitCode == 0 && output.Contains("OK");
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
