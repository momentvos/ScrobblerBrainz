using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TagLib;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();
        private static readonly HttpClient httpClient = new HttpClient();

        public string userToken = "";
        public System.Windows.Forms.TextBox userTokenTextBox;

        public string settingsSubfolder = "ScrobblerBrainz\\";
        public string settingsFile = "usertoken";

        private DateTimeOffset timestamp;
        public string artist = "";
        public string track = "";
        public string release = "";
        public int duration_ms = 0;
        private string currentFileUrl = "";
        private string previousPlaycount;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;

            about.VersionMajor = 2;
            about.VersionMinor = 0;
            about.Revision = 2;

            about.Name = "ScrobblerBrainz";
            about.Description = "A scrobbler for ListenBrainz service, version " + about.VersionMajor + "." + about.VersionMinor + "." + about.Revision;
            about.Author = "karaluh (patched)";
            about.TargetApplication = "";
            about.Type = PluginType.General;

            about.MinInterfaceVersion = 30;
            about.MinApiRevision = 40;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            about.ConfigurationPanelHeight = 30;

            string tokenPath = Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder, settingsFile);
            if (System.IO.File.Exists(tokenPath))
            {
                userToken = System.IO.File.ReadAllText(tokenPath).Trim();
            }

            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Panel.FromHandle(panelHandle);
                Label userTokenLabel = new Label();
                userTokenLabel.AutoSize = true;
                userTokenLabel.Location = new Point(0, 4);
                userTokenLabel.Text = "ListenBrainz user token:";

                userTokenTextBox = new System.Windows.Forms.TextBox();
                userTokenTextBox.Location = new Point(userTokenLabel.Width + 35, 0);
                userTokenTextBox.MaxLength = 64;
                userTokenTextBox.Width = 300;
                userTokenTextBox.BackColor = Color.FromArgb(mbApiInterface.Setting_GetSkinElementColour(
                    SkinElement.SkinInputControl, ElementState.ElementStateDefault, ElementComponent.ComponentBackground));
                userTokenTextBox.ForeColor = Color.FromArgb(mbApiInterface.Setting_GetSkinElementColour(
                    SkinElement.SkinInputControl, ElementState.ElementStateDefault, ElementComponent.ComponentForeground));
                userTokenTextBox.BorderStyle = BorderStyle.FixedSingle;
                userTokenTextBox.Text = userToken;

                configPanel.Controls.AddRange(new Control[] { userTokenLabel, userTokenTextBox });
            }
            return false;
        }

        public void SaveSettings()
        {
            if (userTokenTextBox != null)
            {
                userToken = userTokenTextBox.Text.Trim();
                string dir = Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder);
                Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(Path.Combine(dir, settingsFile), userToken);
            }
        }

        public void Close(PluginCloseReason reason) { }
        public void Uninstall() { }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();

            switch (type)
            {
                case NotificationType.PluginStartup:
                    timestamp = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(mbApiInterface.Player_GetPosition());
                    currentFileUrl = mbApiInterface.NowPlaying_GetFileUrl() ?? "";
                    artist = EscapeJson(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
                    track = EscapeJson(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    release = EscapeJson(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album));
                    duration_ms = mbApiInterface.NowPlaying_GetDuration();
                    previousPlaycount = mbApiInterface.NowPlaying_GetFileProperty(FilePropertyType.PlayCount);

                    if (!string.IsNullOrEmpty(userToken))
                    {
                        string tokenSnapshot = userToken;
                        Task.Run(() => ResendOfflineScrobbles(dataPath, tokenSnapshot));
                    }
                    break;

                case NotificationType.TrackChanged:
                    timestamp = DateTimeOffset.UtcNow;
                    currentFileUrl = mbApiInterface.NowPlaying_GetFileUrl() ?? "";
                    artist = EscapeJson(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
                    track = EscapeJson(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    release = EscapeJson(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album));
                    duration_ms = mbApiInterface.NowPlaying_GetDuration();
                    previousPlaycount = mbApiInterface.NowPlaying_GetFileProperty(FilePropertyType.PlayCount);

                    if (!string.IsNullOrEmpty(userToken) && !string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(track))
                    {
                        string mbids = GenerateMbidJson(currentFileUrl);
                        string nowPlayingJson = BuildPayload("playing_now", 0, artist, track, release, mbids, duration_ms);
                        string tokenSnapshot = userToken;
                        Task.Run(() => SubmitScrobbleAsync(nowPlayingJson, tokenSnapshot, dataPath, 0));
                    }
                    break;

                case NotificationType.PlayCountersChanged:
                    string newPlaycount = mbApiInterface.Library_GetFileProperty(sourceFileUrl, FilePropertyType.PlayCount);
                    if (!string.IsNullOrEmpty(userToken) && previousPlaycount != newPlaycount)
                    {
                        previousPlaycount = newPlaycount;
                        if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(track))
                        {
                            long listenedAt = timestamp.ToUnixTimeSeconds();
                            string mbids = GenerateMbidJson(currentFileUrl);
                            string submitListenJson = BuildPayload("single", listenedAt, artist, track, release, mbids, duration_ms);
                            string tokenSnapshot = userToken;
                            Task.Run(() => SubmitScrobbleAsync(submitListenJson, tokenSnapshot, dataPath, listenedAt));
                        }
                    }
                    break;
            }
        }

        private static string BuildPayload(string listenType, long listenedAt, string escArtist, string escTrack, string escRelease, string mbidJson, int durMs)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"listen_type\": \"").Append(listenType).Append("\", \"payload\": [{");
            if (listenType == "single")
            {
                sb.Append("\"listened_at\": ").Append(listenedAt).Append(", ");
            }
            sb.Append("\"track_metadata\": {");
            sb.Append("\"artist_name\": \"").Append(escArtist).Append("\", ");
            sb.Append("\"track_name\": \"").Append(escTrack).Append("\"");
            if (!string.IsNullOrEmpty(escRelease))
            {
                sb.Append(", \"release_name\": \"").Append(escRelease).Append("\"");
            }
            sb.Append(", \"additional_info\": {");
            sb.Append(mbidJson);
            if (durMs > 0)
            {
                sb.Append("\"duration_ms\": ").Append(durMs).Append(", ");
            }
            sb.Append("\"media_player\": \"MusicBee\", \"submission_client\": \"ScrobblerBrainz\"");
            sb.Append("}}}]}");
            return sb.ToString();
        }

        private static async Task SubmitScrobbleAsync(string json, string token, string dataPath, long unixTs)
        {
            bool isSingle = json.Contains("\"listen_type\": \"single\"");

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.listenbrainz.org/1/submit-listens"))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                        HttpResponseMessage response = await httpClient.SendAsync(request).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            return;
                        }

                        string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        string logDir = Path.Combine(dataPath, "ScrobblerBrainz");
                        Directory.CreateDirectory(logDir);
                        string logPath = Path.Combine(logDir, "error.log");
                        System.IO.File.AppendAllText(logPath, DateTime.Now.ToString("s") + " HTTP " + (int)response.StatusCode + ": " + errorBody + Environment.NewLine + json + Environment.NewLine);

                        if (isSingle)
                        {
                            if (response.StatusCode == HttpStatusCode.BadRequest || i == 4)
                            {
                                SaveScrobbleStatic(dataPath, unixTs.ToString(), json);
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    string logDir = Path.Combine(dataPath, "ScrobblerBrainz");
                    Directory.CreateDirectory(logDir);
                    System.IO.File.AppendAllText(Path.Combine(logDir, "error.log"), DateTime.Now.ToString("s") + " Exception: " + ex.Message + Environment.NewLine);

                    if (isSingle)
                    {
                        SaveScrobbleStatic(dataPath, unixTs.ToString(), json);
                    }
                    return;
                }

                await Task.Delay(2000).ConfigureAwait(false);
            }
        }

        private static async Task ResendOfflineScrobbles(string dataPath, string token)
        {
            string offlineDir = Path.Combine(dataPath, "ScrobblerBrainz", "scrobbles");
            if (!Directory.Exists(offlineDir)) return;

            foreach (string filePath in Directory.GetFiles(offlineDir, "*.json"))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(filePath);
                    using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.listenbrainz.org/1/submit-listens"))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                        HttpResponseMessage response = await httpClient.SendAsync(request).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }
                }
                catch { }
            }
        }

        private string GenerateMbidJson(string fileUrl)
        {
            if (string.IsNullOrEmpty(fileUrl) || !System.IO.File.Exists(fileUrl))
                return "";

            try
            {
                string mbidJson = "";
                using (var file = TagLib.File.Create(fileUrl))
                {
                    string[] trackArtistMbids = new string[0];

                    if (file.GetTag(TagTypes.Xiph) != null)
                    {
                        var fileTags = (TagLib.Ogg.XiphComment)file.GetTag(TagTypes.Xiph);
                        trackArtistMbids = fileTags.GetField("MUSICBRAINZ_ARTISTID") ?? new string[0];
                    }
                    else if (!string.IsNullOrEmpty(file.Tag.MusicBrainzArtistId))
                    {
                        trackArtistMbids = file.Tag.MusicBrainzArtistId.Split(new char[] { ';', '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    }

                    if (trackArtistMbids.Length > 0)
                    {
                        StringBuilder artistSb = new StringBuilder("\"artist_mbids\": [");
                        for (int i = 0; i < trackArtistMbids.Length; i++)
                        {
                            if (i > 0) artistSb.Append(", ");
                            artistSb.Append("\"").Append(EscapeJson(trackArtistMbids[i].Trim())).Append("\"");
                        }
                        artistSb.Append("], ");
                        mbidJson += artistSb.ToString();
                    }

                    if (!string.IsNullOrEmpty(file.Tag.MusicBrainzTrackId))
                        mbidJson += "\"recording_mbid\": \"" + EscapeJson(file.Tag.MusicBrainzTrackId) + "\", ";
                    if (!string.IsNullOrEmpty(file.Tag.MusicBrainzReleaseId))
                        mbidJson += "\"release_mbid\": \"" + EscapeJson(file.Tag.MusicBrainzReleaseId) + "\", ";
                    if (!string.IsNullOrEmpty(file.Tag.MusicBrainzReleaseGroupId))
                        mbidJson += "\"release_group_mbid\": \"" + EscapeJson(file.Tag.MusicBrainzReleaseGroupId) + "\", ";
                    if (file.Tag.Track != 0)
                        mbidJson += "\"tracknumber\": " + file.Tag.Track + ", ";
                }
                return mbidJson;
            }
            catch
            {
                return "";
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public void SaveScrobble(string ts, string json)
        {
            SaveScrobbleStatic(mbApiInterface.Setting_GetPersistentStoragePath(), ts, json);
        }

        private static void SaveScrobbleStatic(string dataPath, string ts, string json)
        {
            string dir = Path.Combine(dataPath, "ScrobblerBrainz", "scrobbles");
            Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(Path.Combine(dir, ts + ".json"), json);
        }
    }
}