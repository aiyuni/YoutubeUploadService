using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;


namespace YoutubeUploadService
{
    [System.ComponentModel.DesignerCategory("")]
    public partial class YoutubeUploadService : ServiceBase
    {
        private FileSystemWatcher _obsDirectoryWatcher;

        public YoutubeUploadService()
        {
            InitializeComponent();
        }

        public void DebugStart()
        {
            OnStart(null);
        }

        protected override async void OnStart(string[] args)
        {
            Console.WriteLine("Service has successfully started!");

            //initialize fileWatcher;
            _obsDirectoryWatcher = new FileSystemWatcher();
            _obsDirectoryWatcher.Path = Constants.ObsDirectory;
            _obsDirectoryWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            _obsDirectoryWatcher.Created += new FileSystemEventHandler(OnVideoRecorded);
            _obsDirectoryWatcher.EnableRaisingEvents = true;

        }

        /// <summary>
        /// Method to upload a local file to Youtube.  Credentials are stored in a separate client_secrets file. 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task UploadToYoutube(string path)
        {
            UserCredential credential;

            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.Load(stream).Secrets,
                    new[] { YouTubeService.Scope.Youtube }, "user", CancellationToken.None,
                    new FileDataStore(this.GetType().ToString()));
            }

            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName =  this.GetType().ToString()
            });

            var video = new Video();
            video.Snippet = new VideoSnippet();
            video.Snippet.Title = "Dota Replay " + DateTime.Now;  //this is the video name 
            video.Snippet.Description = "Automatically uploaded";  //video description
            video.Status = new VideoStatus();
            video.Status.PrivacyStatus = "unlisted";  
            //var filePath = Constants.ObsDirectory + "\\2020-01-14_19-46-35.mp4";
            var filePath = path;

            using (var fileStream = new FileStream(filePath, FileMode.Open))
            {
                var videosInsertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
                videosInsertRequest.ProgressChanged += videosInsertRequest_ProgressChanged;
                videosInsertRequest.ResponseReceived += videosInsertRequest_ResponseReceived;

                await videosInsertRequest.UploadAsync();
            }

            //re-enable filewatcher once the file has successfully been uploaded to youtube.
            _obsDirectoryWatcher.EnableRaisingEvents = true;
        }

        void videosInsertRequest_ProgressChanged(Google.Apis.Upload.IUploadProgress progress)
        {
            switch (progress.Status)
            {
                case UploadStatus.Uploading:
                    Console.WriteLine("{0} bytes sent.", progress.BytesSent);
                    break;

                case UploadStatus.Failed:
                    Console.WriteLine("An error prevented the upload from completing.\n{0}", progress.Exception);
                    break;
            }
        }

        void videosInsertRequest_ResponseReceived(Video video)
        {
            Console.WriteLine("Video id '{0}' was successfully uploaded.", video.Id);
        }

        protected override void OnStop()
        {
        }

        /// <summary>
        /// This method is called when the filewatcher determines that a file has been added to the directory. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnVideoRecorded(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine("A video has been recorded in folder");

            //Wait until the file is finished being written to. 
            while (IsFileLocked(new FileInfo(e.FullPath)))
            {
                Console.Write("Video is still being processed: " + e.FullPath);
                //await Task.Delay(5000);
            }

            Console.Write("Video has finished processing");

            //Temporarily disable filewatcher so that youtube uploading won't trigger the watcher. 
            _obsDirectoryWatcher.EnableRaisingEvents = false;

            //upload to youtube 
            await UploadToYoutube(e.FullPath);
        }

        /// <summary>
        /// Helper method to check if a file is being locked or not.  A file is locked when it is still being used by another process,
        /// i.e when it is still being written to
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        private bool IsFileLocked(FileInfo fileInfo)
        {
            try
            {
                using (FileStream stream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }

                Console.Write("File is not in use anymore!");
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine("File is still in use: " + e.ToString());
                return true;
            }
        }
    }
}
