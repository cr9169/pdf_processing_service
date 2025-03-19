using PdfProcessingService.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace PdfProcessingService.Services
{
    public class FileDownloaderService
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveReadonly };
        private const string ApplicationName = "Google Drive File Downloader";
        private const string CredentialsPath = "credentials.json"; // Ensure this file is present in your project
        private const string TokenPath = "token.json";

        private readonly ILogger<FileDownloaderService> _logger;

        public FileDownloaderService(ILogger<FileDownloaderService> logger)
        {
            _logger = logger;
        }

        public async Task<DownloadResult> DownloadFileAsync(string folderId, string fileName, string localFolderPath)
        {
            Console.WriteLine(folderId);
            var overallStopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("Starting authentication process.");
                var authStopwatch = Stopwatch.StartNew();

                UserCredential credential;
                using (var stream = new FileStream(CredentialsPath, FileMode.Open, FileAccess.Read))
                {
                    // Use FromStream instead of the deprecated Load
                    var googleClientSecrets = GoogleClientSecrets.FromStream(stream);

                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        googleClientSecrets.Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(TokenPath, true)
                    );
                }
                authStopwatch.Stop();
                _logger.LogInformation("Authentication completed in {ElapsedMilliseconds} ms.", authStopwatch.ElapsedMilliseconds);

                _logger.LogInformation("Creating Drive API service.");
                var serviceCreationStopwatch = Stopwatch.StartNew();
                // Create the Drive API service
                var service = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName
                });
                serviceCreationStopwatch.Stop();
                _logger.LogInformation("Drive API service created in {ElapsedMilliseconds} ms.", serviceCreationStopwatch.ElapsedMilliseconds);

                _logger.LogInformation("Searching for file '{FileName}' in folder '{FolderId}'.", fileName, folderId);
                var listStopwatch = Stopwatch.StartNew();
                // Search for the file by name within the specified folder
                var listRequest = service.Files.List();
                listRequest.Q = $"'{folderId}' in parents and name = '{fileName}' and trashed = false";
                listRequest.Fields = "files(id, name)";

                var files = await listRequest.ExecuteAsync();
                listStopwatch.Stop();
                _logger.LogInformation("File search completed in {ElapsedMilliseconds} ms.", listStopwatch.ElapsedMilliseconds);

                var file = files.Files.FirstOrDefault();

                if (file == null)
                {
                    overallStopwatch.Stop();
                    _logger.LogWarning("File '{FileName}' not found. Overall process took {ElapsedMilliseconds} ms.", fileName, overallStopwatch.ElapsedMilliseconds);
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = $"File '{fileName}' not found in folder."
                    };
                }

                _logger.LogInformation("Starting file download for '{FileName}'.", fileName);
                var downloadStopwatch = Stopwatch.StartNew();
                // Download the file to the local path
                var request = service.Files.Get(file.Id);
                var savePath = Path.Combine(localFolderPath, file.Name);

                using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                {
                    await request.DownloadAsync(fileStream);
                }
                downloadStopwatch.Stop();
                _logger.LogInformation("File downloaded in {ElapsedMilliseconds} ms.", downloadStopwatch.ElapsedMilliseconds);

                overallStopwatch.Stop();
                _logger.LogInformation("Overall process completed in {ElapsedMilliseconds} minutes.", (double)overallStopwatch.ElapsedMilliseconds / 60000);

                return new DownloadResult
                {
                    Success = true,
                    FilePath = savePath
                };
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                _logger.LogError("Error during file download process after {ElapsedMilliseconds} ms. Error: {ErrorMessage}",
                    overallStopwatch.ElapsedMilliseconds, ex.Message);
                return new DownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
