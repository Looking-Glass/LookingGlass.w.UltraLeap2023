using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Networking;
using GraphQLClient;

using Object = UnityEngine.Object;

namespace LookingGlass.Blocks {
    public static class LookingGlassUserSystem {
        private static class PlayerPrefKeys {
            public const string AccessToken = "accessToken";
            public const string UserId = "userId";
            public const string Username = "username";
            public const string DisplayName = "displayName";
        }

        private static Lazy<Regex> QuiltSettingsSuffix = new Lazy<Regex>(() =>
            new Regex("_qs(?<quiltColumns>[0-9]*)x(?<quiltRows>[0-9]*)a(?<aspect>[0-9]*\\.?[0-9]*)"));

        private static int maxLoginTime = 60000 * 5;
        private static int checkInterval = 1000;

        private static bool isLoggingIn = false;

        public static bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);
        public static string AccessToken {
            get { return PlayerPrefs.GetString(PlayerPrefKeys.AccessToken); }
            private set { PlayerPrefs.SetString(PlayerPrefKeys.AccessToken, value); }
        }

        public static int UserId {
            get { return PlayerPrefs.GetInt(PlayerPrefKeys.UserId); }
            private set { PlayerPrefs.SetInt(PlayerPrefKeys.UserId, value); }
        }

        public static string Username {
            get { return PlayerPrefs.GetString(PlayerPrefKeys.Username); }
            private set { PlayerPrefs.SetString(PlayerPrefKeys.Username, value); }
        }

        public static string DisplayName {
            get { return PlayerPrefs.GetString(PlayerPrefKeys.DisplayName); }
            private set { PlayerPrefs.SetString(PlayerPrefKeys.DisplayName, value); }
        }

        internal static int MaxLoginTime {
            get { return maxLoginTime; }
            set { maxLoginTime = Mathf.Max(checkInterval, value); }
        }

        internal static int CheckInterval {
            get { return checkInterval; }
            set { checkInterval = Mathf.Clamp(value, 10, maxLoginTime); }
        }

        public static async Task LogIn(Action<object> responseCallback = null) {
            if (isLoggingIn)
                throw new InvalidOperationException("Failed to log in: logging in is already in-progress!");
            isLoggingIn = true;
            int maxLoginTime = LookingGlassUserSystem.maxLoginTime;
            int checkInterval = LookingGlassUserSystem.checkInterval;

            try {
                UnityWebRequest loginRequest = LookingGlassWebRequests.CreateRequestForUserAuthorization();
                OAuthDeviceCodeResponse response = await loginRequest.SendAsync<OAuthDeviceCodeResponse>(NetworkErrorBehaviour.Exception, 30000);
                responseCallback?.Invoke(response);

                if (response != null) {
                    Application.OpenURL(response.verification_uri_complete);

                    await Task.Delay(checkInterval);
                    int awaitedTime = checkInterval;

                    while (isLoggingIn && awaitedTime <= maxLoginTime) {
                        UnityWebRequest isDoneRequest = LookingGlassWebRequests.CreateRequestToCheckDeviceCode(response.device_code);
                        Task delay = Task.Delay(checkInterval);
                        AccessTokenResponse accessTokenResponse = await isDoneRequest.SendAsync<AccessTokenResponse>(NetworkErrorBehaviour.Silent, checkInterval);
                        if (accessTokenResponse != null && !string.IsNullOrEmpty(accessTokenResponse.access_token)) {
                            UserData userData = await LookingGlassWebRequests.SendRequestToGetUserData(accessTokenResponse.access_token);
                            ReLogInImmediate(accessTokenResponse.access_token, userData.id, userData.username, userData.displayName);
                            
                            isLoggingIn = false;
                            responseCallback?.Invoke(response);
                        } else await delay;

                        awaitedTime += checkInterval;
                    }
                    if (!IsLoggedIn)
                        throw new TimeoutException("Login wait time exceed the maximum of " + ((float) maxLoginTime / 1000).ToString("F2") + "sec!");
                }
            } catch (Exception e) {
                Debug.LogException(e);
                throw;
            } finally {
                isLoggingIn = false;
            }
        }

        public static void ReLogInImmediate(string accessToken, int userId, string username, string displayName) {
            try {
                AccessToken = accessToken;
                UserId = userId;
                Username = username;
                DisplayName = displayName;

                Assert.IsFalse(string.IsNullOrEmpty(Username), "The username must be valid to be logged in! Did we parse the data correctly from the user data request?");
                Assert.IsFalse(string.IsNullOrEmpty(DisplayName), "The display name must be valid to be logged in! Did we parse the data correctly from the user data request?");
            } catch (AssertionException e) {
                Debug.LogException(e);
                LogOut();
            }
        }

        public static void LogOut() {
            AccessToken = null;
        }

        public static BlockUploadProgress UploadFileToBlocks(string filePath, string title, string description, int quiltColumns, int quiltRows, float aspect, bool isPublished) => BlockUploadProgress.Start(filePath, title, description, quiltColumns, quiltRows, aspect, isPublished);
        public static BlockUploadProgress UploadFileToBlocks(string filePath, string title, string description, bool isPublished) => BlockUploadProgress.Start(filePath, title, description, isPublished);

        internal static async Task UploadFileToBlocksInternal(string filePath, string title, string description, int quiltColumns, int quiltRows, float aspect, bool isPublished, bool useQuiltSuffix,
            Action<UnityWebRequest> uploadRequestSetter, Action<string> updateProgressText, Action<LogType, string> printResult, Action<CreateQuiltHologramArgs> setArgs, Action<HologramData> setResult) {

            CreateQuiltHologramArgs args = new CreateQuiltHologramArgs() {
                title = title,
                description = description,
                type = HologramType.QUILT,
                aspectRatio = aspect,
                quiltCols = quiltColumns,
                quiltRows = quiltRows,
                quiltTileCount = quiltColumns * quiltRows,
                isPublished = isPublished
            };


            string GetErrorPrefix() => "There was an error uploading.\n\n";
            bool printedExceptionText = false;
            bool hasWarnings = false;
            try {
                if (string.IsNullOrWhiteSpace(title))
                    throw new ArgumentException("The title must not be empty!", nameof(title));
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("", filePath);

                Match m = null;
                if (useQuiltSuffix) {
                    m = QuiltSettingsSuffix.Value.Match(filePath);
                    if (!m.Success) {
                        hasWarnings = true;
                        printResult(LogType.Warning, "No quilt settings in file name detected.\n\nThe hologram was given default values for the Looking Glass Portrait.\n(Ex: _qs8x6a0.75 defines 8 columns x 6 rows at 0.75 aspect ratio)");
                        useQuiltSuffix = false;
                        HologramRenderSettings defaultSettings = HologramRenderSettings.Get(QuiltPreset.Portrait);
                        quiltColumns = defaultSettings.viewColumns;
                        quiltRows = defaultSettings.viewRows;
                        aspect = defaultSettings.aspect;
                    }
                }

                updateProgressText("Getting Upload URL...");
                string fileName = Path.GetFileName(filePath);
                UnityWebRequest getURLRequest = LookingGlassWebRequests.CreateRequestToGetFileUploadURL(fileName);
                Debug.Log("Getting upload file URL from " + getURLRequest.url);

                S3Upload uploadInfo;
                try {
                    uploadInfo = await getURLRequest.SendAsync<S3Upload>(NetworkErrorBehaviour.Exception, 0);
                    args.imageUrl = uploadInfo.url;
                } catch (Exception e) {
                    printedExceptionText = true;
                    printResult(LogType.Error, GetErrorPrefix() + "An error occurred while trying to get the AWS S3 upload URL!\n\n" + e.GetType().Name + ": " + e.Message);
                    Debug.LogError("An error occurred while trying to get the AWS S3 upload URL!\n" + e);
                    Debug.LogException(e);
                    throw;
                }
                Debug.Log("Retrieved upload URL!\n" + uploadInfo.url + "\n");
                updateProgressText("Reading File...");

                //NOTE: We need .NET Standard 2.1+ for File.ReadAllBytesAsync() unfortunately, so we do this instead:
                byte[] fileBytes;
                using (FileStream stream = File.OpenRead(filePath)) {
                    fileBytes = new byte[stream.Length];
                    await stream.ReadAsync(fileBytes, 0, fileBytes.Length);
                }

                args.fileSize = fileBytes.Length;
                updateProgressText("Uploading...");

                //NOTE: We want no timeout for uploading files. They could be quite large
                UnityWebRequest request = LookingGlassWebRequests.CreateRequestToUploadFile(uploadInfo.url, fileBytes);
                Task uploadTask = request.SendAsync(NetworkErrorBehaviour.Exception, 0);
                uploadRequestSetter(request);

                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileBytes);

                args.width = tex.width;
                args.height = tex.height;

                if (useQuiltSuffix) {
                    GroupCollection groups = m.Groups;
                    quiltColumns = int.Parse(groups["quiltColumns"].Value);
                    quiltRows = int.Parse(groups["quiltRows"].Value);
                    aspect = float.Parse(groups["aspect"].Value);
                }

                Object.DestroyImmediate(tex);

                try {
                    await uploadTask;
                } catch (Exception e) {
                    printedExceptionText = true;
                    printResult(LogType.Error, GetErrorPrefix() + "An error occurred while trying to upload to " + uploadInfo.url + "!\n\n" + e.GetType().Name + ": " + e.Message);
                    Debug.LogError("An error occurred while trying to upload to " + uploadInfo.url + "!\naccessToken = " + AccessToken);
                    Debug.LogException(e);
                    throw;
                }

                updateProgressText("Creating Hologram...");
                Debug.Log("Upload complete, now sending a request to create the hologram with your user account...\n\n" + request.downloadHandler.text);
                try {
                    HologramData data = await LookingGlassWebRequests.SendRequestToCreateQuiltHologram(args);
                    setResult(data);
                } catch (Exception e) {
                    printedExceptionText = true;
                    printResult(LogType.Error, GetErrorPrefix() + "An error occurred during the GraphQL request to create the hologram!\n\n" + e.GetType().Name + ": " + e.Message);
                    Debug.LogError("An error occurred during the GraphQL request to create the hologram!");
                    Debug.LogException(e);
                    throw;
                }
                updateProgressText("DONE!");
                if (!hasWarnings)
                    printResult(LogType.Log, "Your hologram has been successfully uploaded and can be viewed here:");
                Debug.Log("DONE!");
            } catch (Exception e) {
                if (!printedExceptionText) {
                    Debug.LogException(e);
                    printResult(LogType.Error, GetErrorPrefix() + e.Message);
                }
                throw;
            }
        }
    }
}
